using YoPay.Application.Matching;
using YoPay.Application.Parsing;
using YoPay.Domain.Enums;

namespace YoPay.UnitTests;

/// <summary>
/// The pipeline's behaviour when a message misbehaves.
///
/// These exist because of a real afternoon spent looking at a dashboard where every
/// message sat in Claimed and no invoice was ever paid. Nothing was logged loudly, nothing
/// was marked, and the system looked idle rather than broken: one message threw, the
/// exception came out of the batch loop, and the rows the worker had already taken stayed
/// taken until the stale sweep let them go - at which point the same message threw again.
///
/// A failure must land on the row that caused it, and the rest of the batch must go
/// through. That is what these tests hold in place.
/// </summary>
public class PaymentPipelineTests
{
    private static readonly IMessageParser Parser = MessageParser.ForBkash();

    private static string Credit(string trxId, string amount = "500.00") =>
        $"You have received Tk {amount} from 01711111111. Fee Tk 0.00. Balance Tk 9,999.00. " +
        $"TrxID {trxId} at 23/09/2026 10:05";

    private static PendingRawEvent Event(string body) => new()
    {
        RawEventId = Guid.CreateVersion7(),
        MerchantId = Guid.CreateVersion7(),
        WalletId = Guid.CreateVersion7(),
        SenderId = "bKash",
        Body = body,
        DeviceReceivedAt = new DateTimeOffset(2026, 9, 23, 10, 5, 0, TimeSpan.FromHours(6)),
    };

    [Fact]
    public async Task A_credit_is_parsed_matched_and_marked()
    {
        var store = new PipelineStoreSpy();
        var settled = Event(Credit("DHD6EYO3HO"));
        store.Batch = [settled];

        var run = await new PaymentPipeline(store, new MatcherSpy(), Parser).RunOnceAsync();

        Assert.Equal(1, run.Processed);
        Assert.Equal(1, run.Settled);
        Assert.Equal(0, run.Failed);
        Assert.Equal(RawEventState.Parsed, store.Marks[settled.RawEventId]);
    }

    [Fact]
    public async Task One_message_that_throws_does_not_stop_the_batch()
    {
        var store = new PipelineStoreSpy();
        var matcher = new MatcherSpy();

        var first = Event(Credit("AAA1111111"));
        var poison = Event(Credit("BBB2222222"));
        var last = Event(Credit("CCC3333333"));

        store.Batch = [first, poison, last];
        store.ThrowOnSave.Add(poison.RawEventId);

        var run = await new PaymentPipeline(store, matcher, Parser).RunOnceAsync();

        Assert.Equal(3, run.Processed);
        Assert.Equal(1, run.Failed);
        Assert.Equal(2, run.Settled);
        Assert.Equal(2, matcher.Calls);
    }

    [Fact]
    public async Task A_message_that_throws_is_never_left_in_Claimed()
    {
        var store = new PipelineStoreSpy();
        var poison = Event(Credit("BBB2222222"));
        store.Batch = [poison];
        store.ThrowOnSave.Add(poison.RawEventId);

        var run = await new PaymentPipeline(store, new MatcherSpy(), Parser).RunOnceAsync();

        Assert.Equal(RawEventState.Unparseable, store.Marks[poison.RawEventId]);
        Assert.NotEqual(RawEventState.Claimed, run.Items[0].State);
    }

    [Fact]
    public async Task The_reason_leaves_the_pipeline_so_the_host_can_log_it()
    {
        var store = new PipelineStoreSpy();
        var poison = Event(Credit("BBB2222222"));
        store.Batch = [poison];
        store.ThrowOnSave.Add(poison.RawEventId);

        var run = await new PaymentPipeline(store, new MatcherSpy(), Parser).RunOnceAsync();

        Assert.Contains("wallet_id", run.Items[0].Error);

        // And the same sentence is on the row, so the queue explains itself without a log.
        Assert.Contains("wallet_id", store.Reasons[poison.RawEventId]);
    }

    [Fact]
    public async Task When_the_store_cannot_even_be_marked_the_row_is_reported_as_claimed()
    {
        var store = new PipelineStoreSpy { MarkThrows = true };
        store.Batch = [Event("total gibberish nobody taught the parser")];

        var run = await new PaymentPipeline(store, new MatcherSpy(), Parser).RunOnceAsync();

        Assert.Equal(1, run.Failed);
        Assert.Equal(RawEventState.Claimed, run.Items[0].State);
        Assert.Contains("store is unwell", run.Items[0].Error);
    }

    [Fact]
    public async Task A_transaction_already_in_the_system_is_not_a_failure()
    {
        var store = new PipelineStoreSpy { DuplicateOnSave = true };
        var matcher = new MatcherSpy();
        store.Batch = [Event(Credit("DHD6EYO3HO"))];

        var run = await new PaymentPipeline(store, matcher, Parser).RunOnceAsync();

        Assert.Equal(0, run.Failed);
        Assert.Equal(MatchOutcome.AlreadyProcessed, run.Items[0].Match);
        Assert.Equal(0, matcher.Calls);
    }

    [Fact]
    public async Task Noise_is_ignored_or_flagged_but_never_a_failure()
    {
        var store = new PipelineStoreSpy();
        store.Batch = [Event("Your OTP is 123456. Do not share it with anyone.")];

        var run = await new PaymentPipeline(store, new MatcherSpy(), Parser).RunOnceAsync();

        Assert.Equal(0, run.Failed);
        Assert.Contains(
            run.Items[0].State,
            new[] { RawEventState.Ignored, RawEventState.Unparseable });
    }

    [Fact]
    public async Task A_payment_that_settles_nothing_says_why_on_the_row()
    {
        // The hardest state in the system to explain from the outside: the message reads
        // perfectly, the row says Parsed, and the invoice is still unpaid. The matcher
        // knows the reason; without this it only ever reached a log.
        var store = new PipelineStoreSpy();
        var arrived = Event(Credit("DHD6EYO3HO"));
        store.Batch = [arrived];

        var matcher = new MatcherSpy
        {
            Verdict = new MatchWorkResult
            {
                Outcome = MatchOutcome.Unmatched,
                Reason = "the payment is stamped 2026-09-23 10:05:00Z and the window closed at 2026-09-23 09:00:00Z.",
            },
        };

        await new PaymentPipeline(store, matcher, Parser).RunOnceAsync();

        Assert.Equal(RawEventState.Parsed, store.Marks[arrived.RawEventId]);
        Assert.Contains("the window closed at", store.Reasons[arrived.RawEventId]);
        Assert.StartsWith("Unmatched:", store.Reasons[arrived.RawEventId]);
    }

    [Fact]
    public async Task A_payment_held_for_review_says_why_too()
    {
        var store = new PipelineStoreSpy();
        var arrived = Event(Credit("DHD6EYO3HO"));
        store.Batch = [arrived];

        var matcher = new MatcherSpy
        {
            Verdict = new MatchWorkResult
            {
                Outcome = MatchOutcome.NeedsReview,
                Reason = "Transaction id has been seen before.",
            },
        };

        await new PaymentPipeline(store, matcher, Parser).RunOnceAsync();

        Assert.Contains("seen before", store.Reasons[arrived.RawEventId]);
    }

    [Fact]
    public async Task A_settled_payment_leaves_no_explanation_behind()
    {
        // Only the exceptions carry a sentence. A reason on a settled row would make the
        // queue noisy in exactly the place it has to be readable.
        var store = new PipelineStoreSpy();
        var arrived = Event(Credit("DHD6EYO3HO"));
        store.Batch = [arrived];

        await new PaymentPipeline(store, new MatcherSpy(), Parser).RunOnceAsync();

        Assert.Null(store.Reasons[arrived.RawEventId]);
    }

    private sealed class PipelineStoreSpy : IPipelineStore
    {
        public List<PendingRawEvent> Batch = [];
        public Dictionary<Guid, RawEventState> Marks = [];
        public Dictionary<Guid, string?> Reasons = [];
        public HashSet<Guid> ThrowOnSave = [];
        public bool MarkThrows;
        public bool DuplicateOnSave;

        public Task<IReadOnlyList<PendingRawEvent>> ClaimUnparsedAsync(
            int batchSize, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingRawEvent>>(Batch);

        public Task MarkStateAsync(
            Guid rawEventId, RawEventState state, string? failureReason = null,
            CancellationToken ct = default)
        {
            if (MarkThrows)
            {
                throw new InvalidOperationException("store is unwell");
            }

            Marks[rawEventId] = state;
            Reasons[rawEventId] = failureReason;
            return Task.CompletedTask;
        }

        public Task<Guid?> SaveParsedAsync(
            PendingRawEvent source, PaymentMethod method, decimal amount, string trxId,
            string? senderMsisdn, decimal? balanceAfter, DateTimeOffset occurredAt,
            decimal confidence, CancellationToken ct = default)
        {
            if (ThrowOnSave.Contains(source.RawEventId))
            {
                // What a missing column or a broken transaction actually looks like.
                throw new InvalidOperationException(
                    "23502: null value in column \"wallet_id\" violates not-null constraint");
            }

            return Task.FromResult<Guid?>(DuplicateOnSave ? null : Guid.CreateVersion7());
        }
    }

    private sealed class MatcherSpy : IPaymentMatcher
    {
        public int Calls { get; private set; }

        /// <summary>What the matcher decides. Settles by default.</summary>
        public MatchWorkResult Verdict { get; set; } = new()
        {
            Outcome = MatchOutcome.Matched,
            InvoiceId = Guid.CreateVersion7(),
            Strategy = MatchStrategy.TrxId,
        };

        public Task<MatchWorkResult> MatchAsync(IncomingPayment payment, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Verdict);
        }
    }
}
