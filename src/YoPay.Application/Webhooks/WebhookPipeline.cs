using YoPay.Application.Abstractions;

namespace YoPay.Application.Webhooks;

public sealed record DeliveryReport
{
    public required Guid DeliveryId { get; init; }
    public required string Url { get; init; }
    public required string EventType { get; init; }
    public int? ResponseCode { get; init; }
    public string? Error { get; init; }
    public bool Succeeded { get; init; }
    public bool DeadLettered { get; init; }
    public DateTimeOffset? RetryAt { get; init; }
}

public sealed record WebhookRunResult
{
    public required int NotificationsFannedOut { get; init; }
    public required int NotificationsWithNowhereToGo { get; init; }
    public required IReadOnlyList<DeliveryReport> Deliveries { get; init; }

    public int Sent => Deliveries.Count(d => d.Succeeded);
    public int Failed => Deliveries.Count(d => !d.Succeeded);
    public int DeadLettered => Deliveries.Count(d => d.DeadLettered);

    public int Worked => NotificationsFannedOut + Deliveries.Count;
}

/// <summary>
/// Carries the notifications the matcher queued out to the merchants who asked for them.
///
/// Two stages, and they are separate on purpose.
///
/// Fan-out turns one notification into one delivery per endpoint. It happens once, in a
/// transaction, and after it the notification is closed. Doing this at send time instead
/// would mean a merchant who adds a second endpoint mid-retry starts receiving deliveries
/// for payments that happened before it existed.
///
/// Sending walks the deliveries that are due. Each one carries its own attempt count and
/// its own backoff, so one endpoint being down does not hold up another merchant's - the
/// failure is on the row, not on the batch.
///
/// Nothing here throws on a bad endpoint. A merchant's server returning 500, or not
/// answering at all, is an expected condition in this system, not an error in it.
/// </summary>
public sealed class WebhookPipeline(IWebhookStore store, IWebhookSender sender, IClock clock)
{
    public async Task<WebhookRunResult> RunOnceAsync(
        int batchSize = 50, CancellationToken ct = default)
    {
        var fannedOut = 0;
        var nowhereToGo = 0;

        foreach (var notification in
                 await store.ClaimPendingNotificationsAsync(batchSize, ct).ConfigureAwait(false))
        {
            var targets = await store
                .FindTargetsAsync(notification.MerchantId, ct)
                .ConfigureAwait(false);

            if (targets.Count == 0)
            {
                // Not an error and not a retry. The merchant has not registered an
                // endpoint, or the one they registered failed the URL guard; either way
                // waiting will not produce one.
                await store.CloseUndeliverableAsync(
                        notification.OutboxId,
                        "No active, validated endpoint is registered for this merchant.",
                        ct)
                    .ConfigureAwait(false);

                nowhereToGo++;
                continue;
            }

            await store.FanOutAsync(notification, targets, ct).ConfigureAwait(false);
            fannedOut++;
        }

        var due = await store.ClaimDueDeliveriesAsync(batchSize, ct).ConfigureAwait(false);
        var reports = new List<DeliveryReport>(due.Count);

        foreach (var delivery in due)
        {
            reports.Add(await AttemptAsync(delivery, ct).ConfigureAwait(false));
        }

        return new WebhookRunResult
        {
            NotificationsFannedOut = fannedOut,
            NotificationsWithNowhereToGo = nowhereToGo,
            Deliveries = reports,
        };
    }

    private async Task<DeliveryReport> AttemptAsync(DueDelivery delivery, CancellationToken ct)
    {
        SendResult result;

        try
        {
            result = await sender.SendAsync(delivery, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The sender is supposed to turn a failed request into a SendResult. If it
            // throws anyway, that is still this merchant's delivery failing, not the
            // whole batch - the same lesson the payment pipeline learned the hard way.
            result = new SendResult { Error = $"{ex.GetType().Name}: {ex.Message}" };
        }

        var attempt = delivery.Attempt + 1;

        if (result.Succeeded)
        {
            await store
                .RecordSuccessAsync(delivery.DeliveryId, result.ResponseCode!.Value, ct)
                .ConfigureAwait(false);

            return new DeliveryReport
            {
                DeliveryId = delivery.DeliveryId,
                Url = delivery.Url,
                EventType = delivery.EventType,
                ResponseCode = result.ResponseCode,
                Succeeded = true,
            };
        }

        var verdict = WebhookRetry.After(attempt, result.ResponseCode, clock.UtcNow);

        var error = result.Error
            ?? (result.ResponseCode is { } code
                ? $"The endpoint answered {code}."
                : "The endpoint did not answer.");

        await store
            .RecordFailureAsync(
                delivery.DeliveryId, attempt, result.ResponseCode, error, verdict.NextAttemptAt, ct)
            .ConfigureAwait(false);

        return new DeliveryReport
        {
            DeliveryId = delivery.DeliveryId,
            Url = delivery.Url,
            EventType = delivery.EventType,
            ResponseCode = result.ResponseCode,
            Error = error,
            Succeeded = false,
            DeadLettered = verdict.GivesUp,
            RetryAt = verdict.NextAttemptAt,
        };
    }
}
