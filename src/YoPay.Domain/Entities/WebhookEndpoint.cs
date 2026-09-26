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

    /// <summary>
    /// The signing secret for this endpoint, encrypted at rest, shown to the merchant
    /// exactly once when the endpoint is registered.
    ///
    /// Its own secret rather than the API credential's, for two reasons. An API key can
    /// be rotated because a developer left, without silently breaking every webhook
    /// signature check on the merchant's server; and a merchant with two endpoints - a
    /// live shop and a staging box - should not have to give the staging box a secret
    /// that can also sign requests to the live API.
    /// </summary>
    public string SecretEncrypted { get; set; } = null!;

    public Merchant Merchant { get; set; } = null!;
}
