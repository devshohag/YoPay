using YoPay.Application.Matching;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Domain.Invoicing;

namespace YoPay.UnitTests;

public class MatchRulesTests
{
    private static ParsedTransaction Tx(decimal amount = 500m, decimal confidence = 1.00m) => new()
    {
        MerchantId = Guid.CreateVersion7(),
        WalletId = Guid.CreateVersion7(),
        RawEventId = Guid.CreateVersion7(),
        Method = PaymentMethod.Bkash,
        Amount = amount,
        TrxId = "AB12CD34EF",
        Confidence = confidence,
        OccurredAt = DateTimeOffset.UtcNow,
    };

    private static PaymentSession Session(decimal expected = 500m) => new()
    {
        InvoiceId = Guid.CreateVersion7(),
        WalletId = Guid.CreateVersion7(),
        ExpectedAmount = expected,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
    };

    [Fact]
    public void A_redelivered_event_returns_already_processed_before_any_other_check()
    {
        // Both flags are set: the transaction id is on the seen list because we put it
        // there ourselves when we matched it the first time. Checking duplicates first
        // would turn our own retry into a fraud review.
        var decision = MatchRules.Decide(
            Tx(), [Session()], transactionAlreadyMatched: true, transactionIdSeenBefore: true,
            AmountTolerance.Exact);

        Assert.Equal(MatchOutcome.AlreadyProcessed, decision.Outcome);
    }

    [Fact]
    public void Low_confidence_never_moves_money()
    {
        var decision = MatchRules.Decide(
            Tx(confidence: 0.55m), [Session()], false, false, AmountTolerance.Exact);

        Assert.Equal(MatchOutcome.NeedsReview, decision.Outcome);
    }

    [Fact]
    public void A_repeated_transaction_id_is_reviewed_not_rejected()
    {
        var decision = MatchRules.Decide(
            Tx(), [Session()], false, transactionIdSeenBefore: true, AmountTolerance.Exact);

        Assert.Equal(MatchOutcome.NeedsReview, decision.Outcome);
    }

    [Fact]
    public void Nothing_waiting_means_unmatched_not_discarded()
    {
        var decision = MatchRules.Decide(Tx(), [], false, false, AmountTolerance.Exact);

        Assert.Equal(MatchOutcome.Unmatched, decision.Outcome);
    }

    [Fact]
    public void Two_candidates_means_the_uniqueness_index_failed_so_a_human_looks()
    {
        var decision = MatchRules.Decide(
            Tx(), [Session(), Session()], false, false, AmountTolerance.Exact);

        Assert.Equal(MatchOutcome.NeedsReview, decision.Outcome);
    }

    [Fact]
    public void Exact_amount_matches()
    {
        var session = Session(500m);

        var decision = MatchRules.Decide(Tx(500m), [session], false, false, AmountTolerance.Exact);

        Assert.Equal(MatchOutcome.Matched, decision.Outcome);
        Assert.Equal(session.InvoiceId, decision.InvoiceId);
        Assert.Equal(MatchStrategy.UniqueAmount, decision.Strategy);
    }

    [Fact]
    public void A_shortfall_is_partial_and_stays_visible()
    {
        var decision = MatchRules.Decide(
            Tx(480m), [Session(500m)], false, false, AmountTolerance.Exact);

        Assert.Equal(MatchOutcome.Partial, decision.Outcome);
        Assert.Contains("480", decision.Reason, StringComparison.Ordinal);
    }
}
