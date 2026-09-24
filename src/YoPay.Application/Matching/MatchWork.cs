using YoPay.Domain.Enums;

namespace YoPay.Application.Matching;

/// <summary>Everything the matcher needs about one incoming payment.</summary>
public sealed record IncomingPayment
{
    public required Guid ParsedTransactionId { get; init; }
    public required Guid WalletId { get; init; }
    public required PaymentMethod Method { get; init; }
    public required decimal Amount { get; init; }
    public required string TrxId { get; init; }
    public required decimal Confidence { get; init; }

    /// <summary>
    /// When the payment happened, as the provider printed it in the message - not when
    /// the server heard about it, and not when the handset did. A phone that was offline
    /// for six hours uploads a payment that was perfectly on time, and this is the field
    /// that keeps it that way.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }
}

public sealed record MatchWorkResult
{
    public required MatchOutcome Outcome { get; init; }
    public Guid? InvoiceId { get; init; }
    public MatchStrategy? Strategy { get; init; }
    public string? Reason { get; init; }
}
