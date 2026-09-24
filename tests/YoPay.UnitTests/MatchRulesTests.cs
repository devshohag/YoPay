using YoPay.Application.Matching;
using YoPay.Domain.Enums;
using YoPay.Domain.Invoicing;

namespace YoPay.UnitTests;

public class MatchRulesTests
{
    private static readonly DateTimeOffset PaidAt = new(2026, 9, 23, 10, 5, 0, TimeSpan.FromHours(6));

    private static IncomingPayment Payment(decimal amount = 500m, decimal confidence = 1.00m) => new()
    {
        ParsedTransactionId = Guid.CreateVersion7(),
        WalletId = Guid.CreateVersion7(),
        Method = PaymentMethod.Bkash,
        Amount = amount,
        TrxId = "DHD6EYO3HO",
        Confidence = confidence,
        OccurredAt = PaidAt,
    };

    private static MatchCandidate Candidate(
        decimal expected = 500m,
        Guid? claimId = null,
        DateTimeOffset? graceUntil = null,
        DateTimeOffset? openedAt = null) => new()
    {
        SessionId = Guid.CreateVersion7(),
        InvoiceId = Guid.CreateVersion7(),
        ExpectedAmount = expected,
        OpenedAt = openedAt ?? PaidAt.AddMinutes(-5),
        GraceUntil = graceUntil ?? PaidAt.AddMinutes(20),
        InvoiceStatus = InvoiceStatus.AwaitingPayment,
        ClaimId = claimId,
    };

    [Fact]
    public void A_payment_made_seconds_after_the_invoice_still_matches()
    {
        // The most ordinary sequence there is: the customer opens the checkout page and
        // pays a few seconds later. It used to fail, and the reason is a detail of the
        // provider's wording rather than anything about the payment.
        //
        // bKash stamps its messages to the minute and no finer, so a payment made at
        // 10:05:41 arrives stamped 10:05:00. If the session opened at 10:05:18, the
        // payment's own timestamp sits 18 seconds BEFORE the window it belongs to, and a
        // strict comparison threw it out - leaving an unpaid invoice, an open window and
        // a perfectly good payment beside it, with "its window has closed" as the only
        // explanation on offer.
        var openedAt = PaidAt.AddSeconds(18);

        var decision = Decide(
            claimed: [Candidate(claimId: Guid.CreateVersion7(), openedAt: openedAt)]);

        Assert.Equal(MatchOutcome.Matched, decision.Outcome);
        Assert.Equal(MatchStrategy.TrxId, decision.Strategy);
    }

    [Fact]
    public void The_allowance_before_opening_does_not_stretch_to_an_unrelated_payment()
    {
        // Two minutes covers a truncated timestamp and two clocks that disagree. An hour
        // is a different payment, and money must not drift onto an invoice that did not
        // exist when it was sent.
        var openedAt = PaidAt.AddHours(1);

        var decision = Decide(
            claimed: [Candidate(claimId: Guid.CreateVersion7(), openedAt: openedAt)]);

        Assert.Equal(MatchOutcome.Unmatched, decision.Outcome);
        Assert.Contains("only opened at", decision.Reason);
    }

    [Fact]
    public void A_late_payment_is_told_apart_from_an_early_one()
    {
        // Same outcome, different cause, and the sentence has to say which - one points
        // at the expiry settings and the other does not.
        var decision = Decide(
            claimed: [Candidate(claimId: Guid.CreateVersion7(), graceUntil: PaidAt.AddMinutes(-1))]);

        Assert.Equal(MatchOutcome.Unmatched, decision.Outcome);
        Assert.Contains("the window closed at", decision.Reason);
    }

    private static MatchDecision Decide(
        IncomingPayment? payment = null,
        IReadOnlyList<MatchCandidate>? claimed = null,
        IReadOnlyList<MatchCandidate>? byAmount = null,
        bool alreadyMatched = false,
        bool trxIdSeenBefore = false) =>
        MatchRules.Decide(
            payment ?? Payment(),
            claimed ?? [],
            byAmount ?? [],
            alreadyMatched,
            trxIdSeenBefore,
            AmountTolerance.Exact);

    [Fact]
    public void A_redelivered_payment_answers_already_processed_before_any_other_check()
    {
        // Both flags set, because we put this transaction id on the duplicate list
        // ourselves when we matched it the first time. Checking duplicates first would
        // turn our own retry into a fraud review.
        var decision = Decide(
            claimed: [Candidate()], alreadyMatched: true, trxIdSeenBefore: true);

        Assert.Equal(MatchOutcome.AlreadyProcessed, decision.Outcome);
    }

    [Fact]
    public void Low_confidence_never_moves_money()
    {
        var decision = Decide(Payment(confidence: 0.55m), claimed: [Candidate()]);

        Assert.Equal(MatchOutcome.NeedsReview, decision.Outcome);
    }

    [Fact]
    public void A_transaction_id_seen_before_goes_to_a_person_rather_than_being_rejected()
    {
        var decision = Decide(claimed: [Candidate()], trxIdSeenBefore: true);

        Assert.Equal(MatchOutcome.NeedsReview, decision.Outcome);
    }

    [Fact]
    public void A_customer_claim_settles_the_invoice_they_typed_it_against()
    {
        var claimId = Guid.CreateVersion7();
        var candidate = Candidate(claimId: claimId);

        var decision = Decide(claimed: [candidate]);

        Assert.Equal(MatchOutcome.Matched, decision.Outcome);
        Assert.Equal(MatchStrategy.TrxId, decision.Strategy);
        Assert.Equal(candidate.InvoiceId, decision.InvoiceId);
        Assert.Equal(claimId, decision.ClaimId);
    }

    [Fact]
    public void A_claim_beats_an_amount_match()
    {
        // The customer told us where the money belongs. That is better evidence than
        // inferring it from the figure, so it wins when both are available.
        var claimed = Candidate(claimId: Guid.CreateVersion7());

        var decision = Decide(claimed: [claimed], byAmount: [Candidate()]);

        Assert.Equal(MatchStrategy.TrxId, decision.Strategy);
        Assert.Equal(claimed.InvoiceId, decision.InvoiceId);
    }

    [Fact]
    public void Two_invoices_claiming_one_transaction_id_go_to_a_person()
    {
        var decision = Decide(claimed: [Candidate(), Candidate()]);

        Assert.Equal(MatchOutcome.NeedsReview, decision.Outcome);
    }

    [Fact]
    public void An_amount_match_settles_when_nothing_was_claimed()
    {
        var candidate = Candidate();

        var decision = Decide(byAmount: [candidate]);

        Assert.Equal(MatchOutcome.Matched, decision.Outcome);
        Assert.Equal(MatchStrategy.UniqueAmount, decision.Strategy);
        Assert.Equal(candidate.InvoiceId, decision.InvoiceId);
    }

    [Fact]
    public void Two_sessions_expecting_one_amount_means_the_index_is_not_holding()
    {
        // The partial unique index makes this impossible. If it happens, guessing would be
        // worse than stopping.
        var decision = Decide(byAmount: [Candidate(), Candidate()]);

        Assert.Equal(MatchOutcome.NeedsReview, decision.Outcome);
        Assert.Contains("uniqueness index", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_waiting_means_unmatched_not_discarded()
    {
        var decision = Decide();

        Assert.Equal(MatchOutcome.Unmatched, decision.Outcome);
    }

    [Fact]
    public void A_payment_that_arrives_after_the_grace_period_does_not_settle()
    {
        var late = Candidate(graceUntil: PaidAt.AddMinutes(-1));

        var decision = Decide(claimed: [late]);

        Assert.Equal(MatchOutcome.Unmatched, decision.Outcome);
        Assert.Contains("the window closed at", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_payment_made_on_time_but_uploaded_hours_late_still_settles()
    {
        // The phone was offline and uploaded at 3am. What matters is when the customer
        // paid, and OccurredAt is the provider's own word for that.
        var decision = Decide(claimed: [Candidate(graceUntil: PaidAt.AddMinutes(10))]);

        Assert.Equal(MatchOutcome.Matched, decision.Outcome);
    }

    [Fact]
    public void A_shortfall_is_partial_and_stays_visible()
    {
        var decision = Decide(Payment(480m), byAmount: [Candidate(500m)]);

        Assert.Equal(MatchOutcome.Partial, decision.Outcome);
        Assert.Contains("480", decision.Reason, StringComparison.Ordinal);
    }
}
