using YoPay.Domain.Common;
using YoPay.Domain.Enums;

namespace YoPay.Domain.Entities;

/// <summary>
/// One attempt to deliver one notification. Backoff runs 1m, 5m, 30m, 2h, 12h and then
/// dead-letters, which surfaces on the merchant's dashboard rather than disappearing.
/// </summary>
public class WebhookDelivery : MerchantEntity
{
    public Guid InvoiceId { get; set; }
    public Guid WebhookEndpointId { get; set; }

    public string PayloadJson { get; set; } = null!;

    /// <summary>Which event this is - payment.paid and the rest. Sent as X-YoPay-Event.</summary>
    public string EventType { get; set; } = null!;

    /// <summary>
    /// The X-YoPay-Signature sent on the most recent attempt, kept for support.
    ///
    /// Not minted when the row is created, which an earlier draft did: the MAC covers a
    /// timestamp, so a signature made at fan-out is already stale by the time a retry
    /// twelve hours later sends it, and the merchant rejects it as replayed. It is
    /// computed per attempt and recorded here afterwards - which is also what makes
    /// "your signature does not verify" answerable rather than a guessing game.
    /// </summary>
    public string? Signature { get; set; }

    public int Attempt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int? ResponseCode { get; set; }
    public string? LastError { get; set; }

    public WebhookEndpoint WebhookEndpoint { get; set; } = null!;
}
