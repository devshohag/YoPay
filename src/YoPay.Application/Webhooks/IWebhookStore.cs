using YoPay.Domain.Enums;

namespace YoPay.Application.Webhooks;

/// <summary>One notification waiting to be turned into deliveries.</summary>
public sealed record PendingNotification
{
    public required Guid OutboxId { get; init; }
    public required Guid MerchantId { get; init; }
    public required Guid InvoiceId { get; init; }
    public required string Type { get; init; }
    public required string PayloadJson { get; init; }
}

/// <summary>Where one notification is to be sent, and with which secret.</summary>
public sealed record EndpointTarget
{
    public required Guid EndpointId { get; init; }
    public required string Url { get; init; }

    /// <summary>Decrypted by the store. It never leaves this process.</summary>
    public required string Secret { get; init; }
}

/// <summary>One attempt that is due now.</summary>
public sealed record DueDelivery
{
    public required Guid DeliveryId { get; init; }
    public required Guid EndpointId { get; init; }
    public required string Url { get; init; }
    public required string Secret { get; init; }
    public required string PayloadJson { get; init; }
    public required string EventType { get; init; }

    /// <summary>Attempts already made. Zero on the first send.</summary>
    public required int Attempt { get; init; }
}

public interface IWebhookStore
{
    /// <summary>
    /// Takes notifications nobody else is working on. SKIP LOCKED, like the payment
    /// pipeline, so a second worker picks up different rows rather than waiting.
    /// </summary>
    Task<IReadOnlyList<PendingNotification>> ClaimPendingNotificationsAsync(
        int batchSize, CancellationToken ct = default);

    /// <summary>Active, validated endpoints for a merchant. An unvalidated or rejected
    /// endpoint is not a target: the URL guard has either not run or has said no.</summary>
    Task<IReadOnlyList<EndpointTarget>> FindTargetsAsync(
        Guid merchantId, CancellationToken ct = default);

    /// <summary>
    /// Writes one delivery row per target and closes the notification, in one
    /// transaction. Split apart, a crash in between either loses the notification or
    /// sends it twice.
    /// </summary>
    Task FanOutAsync(
        PendingNotification notification,
        IReadOnlyList<EndpointTarget> targets,
        CancellationToken ct = default);

    /// <summary>Nowhere to send it. The notification is closed rather than retried, and
    /// says so, because a merchant with no endpoint is a setup problem, not an outage.</summary>
    Task CloseUndeliverableAsync(
        Guid outboxId, string reason, CancellationToken ct = default);

    Task<IReadOnlyList<DueDelivery>> ClaimDueDeliveriesAsync(
        int batchSize, CancellationToken ct = default);

    Task RecordSuccessAsync(
        Guid deliveryId, int responseCode, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="nextAttemptAt"/> null means no attempt is left, and the delivery
    /// dead-letters instead of disappearing.
    /// </summary>
    Task RecordFailureAsync(
        Guid deliveryId,
        int attempt,
        int? responseCode,
        string failureReason,
        DateTimeOffset? nextAttemptAt,
        CancellationToken ct = default);

    /// <summary>Marks an endpoint after the URL guard has had an opinion about it.</summary>
    Task SetEndpointStateAsync(
        Guid endpointId,
        WebhookEndpointState state,
        string? failureReason,
        CancellationToken ct = default);
}
