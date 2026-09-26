using YoPay.Domain.Enums;

namespace YoPay.Contracts.Api;

public sealed record RegisterEndpointRequest
{
    public required string Url { get; init; }
}

/// <summary>
/// The secret is present exactly once, in the answer to the call that created the
/// endpoint. Listing endpoints never returns it, and no other call can produce it again:
/// it is stored encrypted and there is nothing in the system that decrypts it to a
/// response. A merchant who loses it registers the endpoint afresh.
/// </summary>
public sealed record EndpointResponse
{
    public required Guid EndpointId { get; init; }
    public required string Url { get; init; }
    public required WebhookEndpointState State { get; init; }
    public required bool IsActive { get; init; }
    public string? LastFailureReason { get; init; }

    /// <summary>Set only on the response that created the endpoint.</summary>
    public string? Secret { get; init; }
}

public sealed record CancelInvoiceRequest
{
    public Guid? InvoiceId { get; init; }
    public string? OrderRef { get; init; }
}
