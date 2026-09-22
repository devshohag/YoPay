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

    /// <summary>HMAC-SHA256 sent as X-YoPay-Signature so the merchant can verify origin.</summary>
    public string Signature { get; set; } = null!;

    public int Attempt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int? ResponseCode { get; set; }
    public string? LastError { get; set; }

    public WebhookEndpoint WebhookEndpoint { get; set; } = null!;
}
