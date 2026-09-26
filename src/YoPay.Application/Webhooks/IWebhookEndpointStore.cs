using YoPay.Domain.Enums;

namespace YoPay.Application.Webhooks;

public sealed record EndpointView
{
    public required Guid EndpointId { get; init; }
    public required string Url { get; init; }
    public required WebhookEndpointState State { get; init; }
    public required bool IsActive { get; init; }
    public string? LastFailureReason { get; init; }
}

/// <summary>
/// Managing endpoints, which is a different job from delivering to them.
///
/// Separate from IWebhookStore because the dispatcher and the merchant's own API want
/// opposite things: the dispatcher needs decrypted secrets and rows nobody else is
/// holding, and it runs unattended; this is a merchant editing their own settings. One
/// interface carrying both would put "give me the plaintext secret" one autocomplete away
/// from the code that answers an HTTP request.
/// </summary>
public interface IWebhookEndpointStore
{
    Task<IReadOnlyList<EndpointView>> ListAsync(Guid merchantId, CancellationToken ct = default);

    /// <summary>Stores the endpoint with its secret encrypted. The plaintext is the
    /// caller's to return once and then forget.</summary>
    Task<Guid> RegisterAsync(
        Guid merchantId, string url, string secret, CancellationToken ct = default);

    /// <summary>Returns false when the endpoint is not this merchant's.</summary>
    Task<bool> SetActiveAsync(
        Guid merchantId, Guid endpointId, bool isActive, CancellationToken ct = default);
}
