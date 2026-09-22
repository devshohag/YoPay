namespace YoPay.Contracts.Api;

/// <summary>
/// Server-side confirmation, by invoice id or by the merchant's own order reference.
///
/// The SDKs call this before fulfilling an order even when a webhook has already
/// arrived. A webhook is a notification, not proof: it can be forged, and an order
/// shipped on a forged webhook is money gone.
/// </summary>
public sealed record VerifyInvoiceRequest
{
    public Guid? InvoiceId { get; init; }
    public string? OrderRef { get; init; }
}
