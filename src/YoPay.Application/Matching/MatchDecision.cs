using YoPay.Domain.Enums;

namespace YoPay.Application.Matching;

public enum MatchOutcome
{
    /// <summary>Already matched. Return success without touching anything.</summary>
    AlreadyProcessed = 0,
    Matched = 1,
    /// <summary>Money arrived, short of the invoice. Visible, never silently accepted.</summary>
    Partial = 2,
    /// <summary>Nothing was waiting for this. Goes to the unmatched queue, never dropped.</summary>
    Unmatched = 3,
    /// <summary>A human looks at it: duplicate transaction id, low parser confidence,
    /// or more than one candidate where there should never be more than one.</summary>
    NeedsReview = 4,
    /// <summary>Parsed, and deliberately not a payment - cash out, recharge, promo, OTP.</summary>
    Ignored = 5,
}

/// <summary>
/// The matcher's verdict, worked out as a pure function so every rule can be tested
/// without a database, a queue or a phone.
///
/// The persistence side of T8 - advisory lock, single transaction, unique constraints -
/// carries this out. It does not decide anything.
/// </summary>
public sealed record MatchDecision
{
    public required MatchOutcome Outcome { get; init; }
    public Guid? InvoiceId { get; init; }
    public Guid? PaymentSessionId { get; init; }
    public Guid? ClaimId { get; init; }
    public MatchStrategy? Strategy { get; init; }
    public string? Reason { get; init; }

    public static MatchDecision AlreadyProcessed() =>
        new() { Outcome = MatchOutcome.AlreadyProcessed };

    public static MatchDecision Review(string reason) =>
        new() { Outcome = MatchOutcome.NeedsReview, Reason = reason };

    public static MatchDecision Unmatched(string reason) =>
        new() { Outcome = MatchOutcome.Unmatched, Reason = reason };

    public static MatchDecision Ignored(string reason) =>
        new() { Outcome = MatchOutcome.Ignored, Reason = reason };
}
