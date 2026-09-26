namespace YoPay.Application.Webhooks;

/// <summary>What came back from a merchant's server.</summary>
public sealed record SendResult
{
    /// <summary>Null when nothing answered: connection refused, timeout, TLS failure,
    /// or an address the outbound guard would not dial.</summary>
    public int? ResponseCode { get; init; }

    public string? Error { get; init; }

    public bool Succeeded => ResponseCode is { } code && WebhookRetry.IsSuccess(code);
}

/// <summary>
/// Posts one signed body to one merchant URL.
///
/// A port rather than an HttpClient held here, because every request to an address a
/// merchant chose has to go out through the connect callback that filters resolved
/// addresses - and because this layer stays testable without a socket.
/// </summary>
public interface IWebhookSender
{
    Task<SendResult> SendAsync(DueDelivery delivery, CancellationToken ct = default);
}
