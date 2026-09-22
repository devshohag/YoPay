using YoPay.Domain.Common;
using YoPay.Domain.Enums;

namespace YoPay.Domain.Entities;

/// <summary>
/// Where a merchant wants payment notifications delivered.
///
/// The URL is merchant-supplied and YoPay's own worker makes the request, which is a
/// server-side request forgery vector by construction. State is set by the outbound URL
/// guard, and the dispatcher refuses to post anywhere that is not Valid.
/// </summary>
public class WebhookEndpoint : MerchantEntity
{
    public string Url { get; set; } = null!;
    public WebhookEndpointState State { get; set; } = WebhookEndpointState.Unvalidated;
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? LastFailureReason { get; set; }
    public bool IsActive { get; set; } = true;

    public Merchant Merchant { get; set; } = null!;
}
