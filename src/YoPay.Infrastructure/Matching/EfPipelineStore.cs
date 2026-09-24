using Microsoft.EntityFrameworkCore;
using YoPay.Application.Matching;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Infrastructure.Persistence;

namespace YoPay.Infrastructure.Matching;

public sealed class EfPipelineStore(YoPayDbContext db) : IPipelineStore
{
    private const string UniqueViolation = "23505";

    /// <summary>Long enough that a slow batch is not stolen, short enough that a crash
    /// costs minutes rather than a morning.</summary>
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Takes the next batch and marks it taken.
    ///
    /// Written as a lock inside an explicit transaction rather than as one clever
    /// UPDATE ... RETURNING statement. The clever version is shorter and it is what an
    /// earlier draft used - but EF composes over raw SQL, and a data-modifying statement
    /// wrapped in a subquery is a syntax error in Postgres that surfaces only at runtime,
    /// as messages silently stuck in Claimed. Two plain statements inside one transaction
    /// are duller and cannot do that.
    ///
    /// SELECT FOR UPDATE SKIP LOCKED still does the real work: a second worker walks past
    /// the rows this one holds instead of queueing behind them or duplicating them.
    ///
    /// Rows left in Claimed by a crashed worker are released first, which is why that state
    /// exists - a message that stalls visibly and reappears is recoverable, one silently
    /// held forever is not.
    /// </summary>
    public async Task<IReadOnlyList<PendingRawEvent>> ClaimUnparsedAsync(
        int batchSize, CancellationToken ct = default)
    {
        await ReleaseStaleClaimsAsync(ct).ConfigureAwait(false);

        var size = Math.Clamp(batchSize, 1, 500);

        // The connection retries on a transient failure (EnableRetryOnFailure), and a
        // retrying execution strategy refuses a transaction opened behind its back. The
        // whole take-and-mark goes to the strategy as one retriable unit; a retry simply
        // selects again, which is safe because nothing outside the transaction has seen
        // the rows yet.
        var strategy = db.Database.CreateExecutionStrategy();

        var rows = await strategy
            .ExecuteAsync(() => TakeBatchAsync(size, ct))
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return [];
        }

        var deviceIds = rows.Select(r => r.DeviceId).Distinct().ToList();

        var wallets = await db.Devices
            .AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .Select(d => new { d.Id, d.WalletId })
            .ToDictionaryAsync(d => d.Id, d => d.WalletId, ct)
            .ConfigureAwait(false);

        var pending = new List<PendingRawEvent>(rows.Count);

        foreach (var row in rows)
        {
            if (!wallets.TryGetValue(row.DeviceId, out var walletId))
            {
                // The device was deleted under us. Skipping would leave the row Claimed
                // forever and invisible; marking it says so on the dashboard instead.
                await MarkStateAsync(
                        row.Id,
                        RawEventState.Unparseable,
                        "The device this message came from no longer exists.",
                        ct)
                    .ConfigureAwait(false);
                continue;
            }

            pending.Add(new PendingRawEvent
            {
                RawEventId = row.Id,
                MerchantId = row.MerchantId,
                WalletId = walletId,
                SenderId = row.SenderId,
                Body = row.Body,
                DeviceReceivedAt = row.DeviceReceivedAt,
            });
        }

        return pending;
    }

    /// <summary>
    /// One transaction: take the rows nobody else holds, mark them taken, commit.
    ///
    /// FromSql on the set rather than SqlQuery of a scalar, because SqlQuery insists the
    /// column be named Value - the kind of detail that fails at runtime on a Tuesday.
    /// SELECT * lets EF map by column name and brings the batch back in one round trip.
    /// </summary>
    private async Task<List<RawEvent>> TakeBatchAsync(int size, CancellationToken ct)
    {
        await using var transaction = await db.Database
            .BeginTransactionAsync(ct)
            .ConfigureAwait(false);

        var rows = await db.RawEvents
            .FromSql(
                $"""
                 SELECT * FROM yopay.raw_events
                 WHERE state = {(int)RawEventState.Received}
                 ORDER BY server_received_at
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

        await db.RawEvents
            .Where(e => ids.Contains(e.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.State, RawEventState.Claimed), ct)
            .ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return rows;
    }

    public async Task MarkStateAsync(
        Guid rawEventId,
        RawEventState state,
        string? failureReason = null,
        CancellationToken ct = default)
    {
        // Truncated rather than rejected: a reason that does not fit is still worth more
        // than a row that fails to update because the message was long.
        var reason = failureReason is { Length: > 500 }
            ? failureReason[..500]
            : failureReason;

        await db.RawEvents
            .Where(e => e.Id == rawEventId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.State, state)
                    .SetProperty(e => e.FailureReason, reason),
                ct)
            .ConfigureAwait(false);
    }

    public async Task<Guid?> SaveParsedAsync(
        PendingRawEvent source,
        PaymentMethod method,
        decimal amount,
        string trxId,
        string? senderMsisdn,
        decimal? balanceAfter,
        DateTimeOffset occurredAt,
        decimal confidence,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var parsed = new ParsedTransaction
        {
            MerchantId = source.MerchantId,
            WalletId = source.WalletId,
            RawEventId = source.RawEventId,
            Method = method,
            Amount = amount,
            TrxId = trxId,
            SenderMsisdn = senderMsisdn,
            BalanceAfter = balanceAfter,
            // Npgsql refuses a DateTimeOffset with a non-zero offset for timestamptz
            // rather than dropping the offset quietly, and the exception it throws is
            // wrapped in a DbUpdateException that says nothing useful. Postgres stores
            // the instant either way, so normalising at the boundary costs nothing and
            // means no caller has to know this.
            OccurredAt = occurredAt.ToUniversalTime(),
            Confidence = confidence,
        };

        db.ParsedTransactions.Add(parsed);

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return parsed.Id;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // (method, trx_id) is unique across the whole system. The hold that precedes a
            // merchant payment carries the same id as the completion, and a handset
            // re-uploads its backlog, so arriving here is ordinary rather than an error.
            db.Entry(parsed).State = EntityState.Detached;
            return null;
        }
        catch
        {
            // Any other failure leaves the row tracked and pending, so the next message in
            // the same batch would try to save it again and fail for a reason that has
            // nothing to do with it. Detach first, then let the real error through.
            db.Entry(parsed).State = EntityState.Detached;
            throw;
        }
    }

    private async Task ReleaseStaleClaimsAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - ClaimTimeout;

        await db.RawEvents
            .Where(e => e.State == RawEventState.Claimed && e.ServerReceivedAt < cutoff)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.State, RawEventState.Received), ct)
            .ConfigureAwait(false);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException) as string
            == UniqueViolation;
}
