using YoPay.Domain.Enums;

namespace YoPay.Application.Invoicing;

/// <summary>
/// What the customer is shown. A projection, not the invoice entity: the callback url,
/// the merchant metadata and the customer's own contact details have no business being
/// one careless template binding away from a public page.
/// </summary>
public sealed record CheckoutView
{
    public required Guid InvoiceId { get; init; }
    public required string MerchantName { get; init; }
    public required string OrderRef { get; init; }

    /// <summary>What the merchant billed.</summary>
    public required decimal Amount { get; init; }

    /// <summary>What the customer must actually send. Always the figure on the page.</summary>
    public required decimal ChargedAmount { get; init; }

    public required string Currency { get; init; }
    public required PaymentMethod Method { get; init; }
    public required string PayToNumber { get; init; }
    public required WalletAccountType AccountType { get; init; }
    public required InvoiceStatus Status { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required DateTimeOffset GraceUntil { get; init; }
    public string? RedirectUrl { get; init; }

    public bool IsSettled => Status is InvoiceStatus.Paid or InvoiceStatus.Settled;
}
