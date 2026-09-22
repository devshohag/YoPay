using YoPay.Domain.Common;
using YoPay.Domain.Enums;

namespace YoPay.Domain.Entities;

/// <summary>
/// One payment request from a merchant.
///
/// Amount is what the merchant billed. ChargedAmount is what the customer is actually
/// told to send - the two differ only when the unique-amount strategy salts the figure,
/// and the checkout page always shows ChargedAmount so nobody is surprised at
/// reconciliation time.
/// </summary>
public class Invoice : MerchantEntity
{
    public Guid WalletId { get; set; }

    /// <summary>The merchant's own order identifier. Unique per merchant.</summary>
    public string OrderRef { get; set; } = null!;

    public decimal Amount { get; set; }
    public decimal ChargedAmount { get; set; }
    public string Currency { get; set; } = "BDT";

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Created;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Grace beyond ExpiresAt during which a late payment still matches. Operator
    /// messages run minutes behind and an offline device uploads hours late, so an
    /// invoice moves to PendingReview here rather than failing.
    /// </summary>
    public DateTimeOffset GraceUntil { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerMsisdn { get; set; }

    public string? RedirectUrl { get; set; }
    public string? CallbackUrl { get; set; }

    /// <summary>Merchant-supplied JSON echoed back on the webhook. Opaque to YoPay.</summary>
    public string? MetadataJson { get; set; }

    public Wallet Wallet { get; set; } = null!;
    public ICollection<PaymentSession> Sessions { get; set; } = new List<PaymentSession>();
}
