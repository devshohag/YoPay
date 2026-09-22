using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Domain.Invoicing;

namespace YoPay.Application.Matching;

/// <summary>
/// Which candidate wins, and when nothing should win at all.
///
/// Every rule here exists because getting it wrong costs a merchant real money in one
/// direction or a customer real money in the other, so each one is stated once, here,
/// and tested.
/// </summary>
public static class MatchRules
{
    /// <summary>Below this the parser is guessing, and a guess must not move money.</summary>
    public const decimal MinimumConfidence = 0.80m;

    public static MatchDecision Decide(
        ParsedTransaction transaction,
        IReadOnlyList<PaymentSession> openSessions,
        bool transactionAlreadyMatched,
        bool transactionIdSeenBefore,
        AmountTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(openSessions);

        // First, before anything else. A redelivered event for a payment already handled
        // is the single most common thing that reaches this function, and treating it as
        // suspicious - which is what checking the duplicate list first would do - turns
        // an ordinary retry into a support ticket about a blocked payment.
        if (transactionAlreadyMatched)
        {
            return MatchDecision.AlreadyProcessed();
        }

        if (transaction.Confidence < MinimumConfidence)
        {
            return MatchDecision.Review(
                $"Parser confidence {transaction.Confidence:0.00} is below {MinimumConfidence:0.00}.");
        }

        // A transaction id seen before that is not ours is usually a replay, occasionally
        // fraud, and is never auto-rejected: a person decides.
        if (transactionIdSeenBefore)
        {
            return MatchDecision.Review("Transaction id has been seen before.");
        }

        if (openSessions.Count == 0)
        {
            return MatchDecision.Unmatched("No open session expects this amount.");
        }

        // The partial unique index on (wallet_id, expected_amount) makes this impossible.
        // If it happens anyway the index is gone or wrong, and guessing would be worse
        // than stopping.
        if (openSessions.Count > 1)
        {
            return MatchDecision.Review(
                $"{openSessions.Count} open sessions expect this amount; the uniqueness index is not holding.");
        }

        var session = openSessions[0];
        var full = tolerance.IsFullPayment(session.ExpectedAmount, transaction.Amount);

        return new MatchDecision
        {
            Outcome = full ? MatchOutcome.Matched : MatchOutcome.Partial,
            InvoiceId = session.InvoiceId,
            PaymentSessionId = session.Id,
            Strategy = MatchStrategy.UniqueAmount,
            Reason = full
                ? null
                : $"Received {transaction.Amount} against expected {session.ExpectedAmount}.",
        };
    }
}
