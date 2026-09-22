using YoPay.Domain.Enums;

namespace YoPay.Contracts.Webhooks;

/// <summary>
/// Notification sent to the merchant. Signed with HMAC-SHA256 in X-YoPay-Signature over
/// the raw body; the SDKs verify it, and the docs say plainly that verification is not
/// optional.
/// </summary>
public sealed record PaymentWebhookPayload
{
    public required string Event { get; init; }
    public required Guid InvoiceId { get; init; }
    public required string OrderRef { get; init; }
    public required InvoiceStatus Status { get; init; }
    public required decimal Amount { get; init; }
    public decimal? ReceivedAmount { get; init; }
    public string? TrxId { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
    public required DateTimeOffset SentAt { get; init; }
    public string? MetadataJson { get; init; }
}

public static class WebhookEvents
{
    public const string PaymentPaid = "payment.paid";
    public const string PaymentPartial = "payment.partial";
    public const string PaymentExpired = "payment.expired";
    public const string PaymentPendingReview = "payment.pending_review";
}
