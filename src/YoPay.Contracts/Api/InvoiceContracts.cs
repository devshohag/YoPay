using YoPay.Domain.Enums;

namespace YoPay.Contracts.Api;

/// <summary>Merchant asks YoPay to collect a payment. Filled in T4.</summary>
public sealed record CreateInvoiceRequest
{
    public required string OrderRef { get; init; }
    public required decimal Amount { get; init; }
    public required PaymentMethod Method { get; init; }
    public Guid? WalletId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerMsisdn { get; init; }
    public string? RedirectUrl { get; init; }
    public string? CallbackUrl { get; init; }
    public string? MetadataJson { get; init; }

    /// <summary>
    /// Leave it unset for the transaction-id flow, which is the default.
    ///
    /// UniqueAmount is the zero-input experience: the customer is shown one exact figure
    /// and told to send it, and nothing is typed. YoPay may not be able to grant it - if
    /// every figure near this price is already reserved by another open invoice on the
    /// same wallet, the answer comes back on TrxId instead, and the response says so.
    /// Read Mode on the response rather than assuming you got what you asked for.
    /// </summary>
    public MatchingMode? Mode { get; init; }
}

public sealed record InvoiceResponse
{
    public required Guid InvoiceId { get; init; }
    public required string OrderRef { get; init; }
    public required decimal Amount { get; init; }

    /// <summary>What the customer is actually told to send. Always show this figure.</summary>
    public required decimal ChargedAmount { get; init; }

    public required string Currency { get; init; }
    public required InvoiceStatus Status { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public string? CheckoutUrl { get; init; }

    /// <summary>Which flow the customer will actually see. Not always what was asked for.</summary>
    public MatchingMode Mode { get; init; }
}

/// <summary>
/// Server-side confirmation. The SDKs call this before fulfilling an order even after a
/// webhook arrives, because a webhook alone can be forged and an order shipped on a
/// forged webhook is money gone.
/// </summary>
public sealed record VerifyInvoiceResponse
{
    public required Guid InvoiceId { get; init; }
    public required InvoiceStatus Status { get; init; }
    public decimal? ReceivedAmount { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
    public string? TrxId { get; init; }
}
