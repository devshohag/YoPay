using YoPay.Domain.Enums;
using YoPay.Domain.Invoicing;

namespace YoPay.Application.Matching;

/// <summary>
/// Which candidate wins, and when nothing should win at all.
///
/// Pure, so every rule below can be tested without a database, a queue or a phone. The
/// persistence side carries the verdict out; it decides nothing.
///
/// The order of the checks is itself a rule. An already-processed payment is answered
/// first, before the duplicate list, because the most common thing that reaches this
/// function is our own redelivery - and we put that transaction id on the duplicate list
/// ourselves when we matched it the first time. Checking duplicates first would turn
/// every retry into a fraud review.
/// </summary>
public static class MatchRules
{
    /// <summary>Below this the parser is guessing, and a guess must not move money.</summary>
    public const decimal MinimumConfidence = 0.80m;

    public static MatchDecision Decide(
        IncomingPayment payment,
        IReadOnlyList<MatchCandidate> claimed,
        IReadOnlyList<MatchCandidate> byAmount,
        bool alreadyMatched,
        bool trxIdSeenBefore,
        AmountTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(claimed);
        ArgumentNullException.ThrowIfNull(byAmount);

        if (alreadyMatched)
        {
            return MatchDecision.AlreadyProcessed();
        }

        if (payment.Confidence < MinimumConfidence)
        {
            return MatchDecision.Review(
                $"Parser confidence {payment.Confidence:0.00} is below {MinimumConfidence:0.00}.");
        }

        if (trxIdSeenBefore)
        {
            // Usually a replay, occasionally fraud, never auto-rejected: a person decides.
            return MatchDecision.Review("Transaction id has been seen before.");
        }

        // A customer who typed this transaction id against an invoice has told us exactly
        // where the money belongs. That beats inferring it from the amount, so it is tried
        // first - and it is the only strategy in use through the pilot.
        var claimedInWindow = InWindow(claimed, payment.OccurredAt);
        if (claimedInWindow.Count == 1)
        {
            return Settle(claimedInWindow[0], payment, MatchStrategy.TrxId, tolerance);
        }

        if (claimedInWindow.Count > 1)
        {
            return MatchDecision.Review(
                $"{claimedInWindow.Count} invoices claim transaction {payment.TrxId}.");
        }

        var amountInWindow = InWindow(byAmount, payment.OccurredAt);

        if (amountInWindow.Count == 0)
        {
            return MatchDecision.Unmatched(
                claimed.Count > 0
                    ? DescribeWindowMiss(claimed[0], payment.OccurredAt)
                    : "No open session expects this amount.");
        }

        // The partial unique index on (wallet_id, expected_amount) makes this impossible.
        // If it happens the index is gone or wrong, and guessing would be worse than
        // stopping.
        if (amountInWindow.Count > 1)
        {
            return MatchDecision.Review(
                $"{amountInWindow.Count} open sessions expect {payment.Amount}; " +
                "the uniqueness index is not holding.");
        }

        return Settle(amountInWindow[0], payment, MatchStrategy.UniqueAmount, tolerance);
    }

    /// <summary>
    /// Says which side of the window the payment fell on, with all three times in it.
    ///
    /// "Its window has closed" was the old wording, and it was wrong half the time: a
    /// payment stamped before the session opened got the same sentence, which sent the
    /// reader looking at expiry settings for a problem that was nowhere near them.
    /// </summary>
    private static string DescribeWindowMiss(MatchCandidate candidate, DateTimeOffset eventTime) =>
        candidate.IsBeforeOpening(eventTime)
            ? $"A claim exists for this transaction id, but the payment is stamped " +
              $"{eventTime:u} and the invoice only opened at {candidate.OpenedAt:u}."
            : $"A claim exists for this transaction id, but the payment is stamped " +
              $"{eventTime:u} and the window closed at {candidate.GraceUntil:u}.";

    private static List<MatchCandidate> InWindow(
        IReadOnlyList<MatchCandidate> candidates, DateTimeOffset eventTime)
    {
        var result = new List<MatchCandidate>();

        foreach (var candidate in candidates)
        {
            if (candidate.Accepts(eventTime))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static MatchDecision Settle(
        MatchCandidate candidate,
        IncomingPayment payment,
        MatchStrategy strategy,
        AmountTolerance tolerance)
    {
        var full = tolerance.IsFullPayment(candidate.ExpectedAmount, payment.Amount);

        return new MatchDecision
        {
            Outcome = full ? MatchOutcome.Matched : MatchOutcome.Partial,
            InvoiceId = candidate.InvoiceId,
            PaymentSessionId = candidate.SessionId,
            ClaimId = candidate.ClaimId,
            Strategy = strategy,
            Reason = full
                ? null
                : $"Received {payment.Amount} against expected {candidate.ExpectedAmount}.",
        };
    }
}
