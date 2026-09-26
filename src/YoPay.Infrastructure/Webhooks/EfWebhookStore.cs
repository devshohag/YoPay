using Microsoft.EntityFrameworkCore;
using YoPay.Application.Abstractions;
using YoPay.Application.Webhooks;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Infrastructure.Persistence;

namespace YoPay.Infrastructure.Webhooks;

/// <summary>
/// The database side of the dispatcher.
///
/// Every transaction here goes through the execution strategy. The connection retries on
/// a transient failure, and a retrying strategy refuses a transaction opened behind its
/// back - the payment pipeline learned that by silently settling nothing for two days,
/// and this file was written afterwards.
/// </summary>
public sealed class EfWebhookStore(
    YoPayDbContext db, ISecretProtector protector, IClock clock) : IWebhookStore
{
    /// <summary>Long enough for a slow batch, short enough that a crash costs minutes.</summary>
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How many times fan-out may fail before a notification is set aside.
    ///
    /// Fan-out only reads endpoints and writes rows, so failing it repeatedly means the
    /// database is unwell rather than the merchant. Ten is enough to ride out a restart
    /// and few enough that a genuinely poisoned row stops costing a worker every poll.
    /// </summary>
    private const int MaxFanOutAttempts = 10;

    public async Task<IReadOnlyList<PendingNotification>> ClaimPendingNotificationsAsync(
        int batchSize, CancellationToken ct = default)
    {
        await SetAsidePoisonedAsync(ct).ConfigureAwait(false);

        var size = Math.Clamp(batchSize, 1, 500);

        var rows = await db.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(() => TakeNotificationsAsync(size, ct))
            .ConfigureAwait(false);

        return rows
            .Select(r => new PendingNotification
            {
                OutboxId = r.Id,
                MerchantId = r.MerchantId,
                InvoiceId = r.InvoiceId,
                Type = r.Type,
                PayloadJson = r.PayloadJson,
            })
            .ToList();
    }

    private async Task<List<OutboxMessage>> TakeNotificationsAsync(int size, CancellationToken ct)
    {
        await using var transaction = await db.Database
            .BeginTransactionAsync(ct)
            .ConfigureAwait(false);

        var now = clock.UtcNow;

        var rows = await db.OutboxMessages
            .FromSql(
                $"""
                 SELECT * FROM yopay.outbox_messages
                 WHERE status = {(int)OutboxStatus.Pending}
                   AND next_attempt_at <= {now}
                   AND attempt < {MaxFanOutAttempts}
                 ORDER BY occurred_at
                 LIMIT {size}
                 FOR UPDATE SKIP LOCKED
                 """)
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return rows;
        }

        var ids = rows.Select(r => r.Id).ToList();

        // A lease rather than a state change: the row stays Pending and simply becomes
        // invisible for the length of the claim. If this worker dies mid fan-out the row
        // comes back on its own, with no sweeper to write and no state that means
        // "someone was holding this when the power went out".
        await db.OutboxMessages
            .Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.NextAttemptAt, now + ClaimTimeout)
                    .SetProperty(m => m.Attempt, m => m.Attempt + 1),
                ct)
            .ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return rows;
    }

    public async Task<IReadOnlyList<EndpointTarget>> FindTargetsAsync(
        Guid merchantId, CancellationToken ct = default)
    {
        var endpoints = await db.WebhookEndpoints
            .AsNoTracking()
            .Where(e => e.MerchantId == merchantId
                        && e.IsActive
                        && e.State == WebhookEndpointState.Valid)
            .Select(e => new { e.Id, e.Url, e.SecretEncrypted })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var targets = new List<EndpointTarget>(endpoints.Count);

        foreach (var endpoint in endpoints)
        {
            string secret;

            try
            {
                secret = protector.Unprotect(endpoint.SecretEncrypted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A secret encrypted under a key this process no longer has. Signing with
                // nothing would be worse than not sending: the merchant would receive a
                // payload their verification rejects, which looks exactly like an attack.
                await SetEndpointStateAsync(
                        endpoint.Id,
                        WebhookEndpointState.Rejected,
                        "The signing secret could not be decrypted. Re-register this endpoint.",
                        ct)
                    .ConfigureAwait(false);

                continue;
            }

            targets.Add(new EndpointTarget
            {
                EndpointId = endpoint.Id,
                Url = endpoint.Url,
                Secret = secret,
            });
        }

        return targets;
    }

    public async Task FanOutAsync(
        PendingNotification notification,
        IReadOnlyList<EndpointTarget> targets,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(targets);

        await db.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();

                await using var transaction = await db.Database
                    .BeginTransactionAsync(ct)
                    .ConfigureAwait(false);

                foreach (var target in targets)
                {
                    db.WebhookDeliveries.Add(new WebhookDelivery
                    {
                        MerchantId = notification.MerchantId,
                        InvoiceId = notification.InvoiceId,
                        WebhookEndpointId = target.EndpointId,
                        PayloadJson = notification.PayloadJson,

                        EventType = notification.Type,
                        Attempt = 0,
                        NextRetryAt = clock.UtcNow,
                        Status = DeliveryStatus.Pending,
                    });
                }

                await db.OutboxMessages
                    .Where(m => m.Id == notification.OutboxId)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(m => m.Status, OutboxStatus.Dispatched)
                            .SetProperty(m => m.ProcessedAt, clock.UtcNow),
                        ct)
                    .ConfigureAwait(false);

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            })
            .ConfigureAwait(false);
    }

    public async Task CloseUndeliverableAsync(
        Guid outboxId, string reason, CancellationToken ct = default)
    {
        await db.OutboxMessages
            .Where(m => m.Id == outboxId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Status, OutboxStatus.DeadLettered)
                    .SetProperty(m => m.ProcessedAt, clock.UtcNow)
                    .SetProperty(m => m.LastError, reason),
                ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DueDelivery>> ClaimDueDeliveriesAsync(
        int batchSize, CancellationToken ct = default)
    {
        var size = Math.Clamp(batchSize, 1, 500);
        var now = clock.UtcNow;

        var rows = await db.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(() => TakeDeliveriesAsync(size, now, ct))
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return [];
        }

        var endpointIds = rows.Select(r => r.WebhookEndpointId).Distinct().ToList();

        var endpoints = await db.WebhookEndpoints
            .AsNoTracking()
            .Where(e => endpointIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Url, e.SecretEncrypted })
            .ToDictionaryAsync(e => e.Id, ct)
            .ConfigureAwait(false);

        var due = new List<DueDelivery>(rows.Count);

        foreach (var row in rows)
        {
            if (!endpoints.TryGetValue(row.WebhookEndpointId, out var endpoint))
            {
                await RecordFailureAsync(
                        row.Id, row.Attempt + 1, null,
                        "The endpoint this delivery belongs to no longer exists.", null, ct)
                    .ConfigureAwait(false);

                continue;
            }

            string secret;

            try
            {
                secret = protector.Unprotect(endpoint.SecretEncrypted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await RecordFailureAsync(
                        row.Id, row.Attempt + 1, null,
                        "The signing secret could not be decrypted.", null, ct)
                    .ConfigureAwait(false);

                continue;
            }

            due.Add(new DueDelivery
            {
                DeliveryId = row.Id,
                EndpointId = endpoint.Id,
                Url = endpoint.Url,
                Secret = secret,
                PayloadJson = row.PayloadJson,
                EventType = row.EventType,
                Attempt = row.Attempt,
            });
        }

        return due;
    }

    private async Task<List<WebhookDelivery>> TakeDeliveriesAsync(
        int size, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database
            .BeginTransactionAsync(ct)
            .ConfigureAwait(false);

        var rows = await db.WebhookDeliveries
            .FromSql(
                $"""
                 SELECT * FROM yopay.webhook_deliveries
                 WHERE status = {(int)DeliveryStatus.Pending}
                   AND next_retry_at <= {now}
                 ORDER BY next_retry_at
                 LIMIT {size}
                 FOR UPDATE SKIP LOCKED
                 """)
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return rows;
        }

        var ids = rows.Select(r => r.Id).ToList();

        // Pushed out of reach for the length of the attempt. Without this a slow POST and
        // a fast poll deliver the same notification twice, which for a shop that ships on
        // webhook means shipping twice.
        await db.WebhookDeliveries
            .Where(d => ids.Contains(d.Id))
            .ExecuteUpdateAsync(
                s => s.SetProperty(d => d.NextRetryAt, now + ClaimTimeout), ct)
            .ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return rows;
    }

    public async Task RecordSuccessAsync(
        Guid deliveryId, int responseCode, CancellationToken ct = default)
    {
        await db.WebhookDeliveries
            .Where(d => d.Id == deliveryId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(d => d.Status, DeliveryStatus.Succeeded)
                    .SetProperty(d => d.Attempt, d => d.Attempt + 1)
                    .SetProperty(d => d.ResponseCode, responseCode)
                    .SetProperty(d => d.NextRetryAt, (DateTimeOffset?)null)
                    .SetProperty(d => d.LastError, (string?)null),
                ct)
            .ConfigureAwait(false);
    }

    public async Task RecordFailureAsync(
        Guid deliveryId,
        int attempt,
        int? responseCode,
        string failureReason,
        DateTimeOffset? nextAttemptAt,
        CancellationToken ct = default)
    {
        var status = nextAttemptAt is null ? DeliveryStatus.DeadLettered : DeliveryStatus.Pending;

        var trimmed = failureReason is { Length: > 1000 }
            ? failureReason[..1000]
            : failureReason;

        await db.WebhookDeliveries
            .Where(d => d.Id == deliveryId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(d => d.Status, status)
                    .SetProperty(d => d.Attempt, attempt)
                    .SetProperty(d => d.ResponseCode, responseCode)
                    .SetProperty(d => d.LastError, trimmed)
                    .SetProperty(d => d.NextRetryAt, nextAttemptAt),
                ct)
            .ConfigureAwait(false);
    }

    public async Task SetEndpointStateAsync(
        Guid endpointId,
        WebhookEndpointState state,
        string? failureReason,
        CancellationToken ct = default)
    {
        await db.WebhookEndpoints
            .Where(e => e.Id == endpointId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.State, state)
                    .SetProperty(e => e.LastCheckedAt, clock.UtcNow)
                    .SetProperty(e => e.LastFailureReason, failureReason),
                ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Takes a notification out of the rotation once fan-out has failed too many times.
    ///
    /// Dead-lettered rather than deleted or left spinning: it stays on the screen, with
    /// the reason, for a person to look at. A queue that quietly drops work is worse than
    /// one that stops.
    /// </summary>
    private async Task SetAsidePoisonedAsync(CancellationToken ct)
    {
        await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending && m.Attempt >= MaxFanOutAttempts)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Status, OutboxStatus.DeadLettered)
                    .SetProperty(m => m.ProcessedAt, clock.UtcNow)
                    .SetProperty(
                        m => m.LastError,
                        $"Fan-out failed {MaxFanOutAttempts} times and was set aside."),
                ct)
            .ConfigureAwait(false);
    }
}
