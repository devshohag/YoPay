using YoPay.Application.Parsing;
using YoPay.Domain.Enums;

namespace YoPay.Application.Matching;

/// <summary>What happened to one message.</summary>
public sealed record PipelineItemResult
{
    public required Guid RawEventId { get; init; }
    public required RawEventState State { get; init; }
    public MessageKind Kind { get; init; }
    public string? TrxId { get; init; }
    public decimal? Amount { get; init; }
    public MatchOutcome? Match { get; init; }
    public Guid? InvoiceId { get; init; }
    public MatchStrategy? Strategy { get; init; }
    public string? Reason { get; init; }

    /// <summary>Set when this message threw rather than reached a decision. Present here
    /// instead of only in a log line because the host has to be able to shout about it.</summary>
    public string? Error { get; init; }
}

public sealed record PipelineRunResult
{
    public required IReadOnlyList<PipelineItemResult> Items { get; init; }

    public int Processed => Items.Count;
    public int Settled => Items.Count(i => i.Match is MatchOutcome.Matched or MatchOutcome.Partial);
    public int Failed => Items.Count(i => i.Error is not null);
    public int NeedsAttention => Items.Count(i =>
        i.State == RawEventState.Unparseable ||
        i.Match is MatchOutcome.NeedsReview or MatchOutcome.Unmatched);
}

/// <summary>
/// Reads the messages a handset uploaded and settles the ones that are payments.
///
/// Two stages, deliberately separate. Parsing turns text into fields and can be re-run
/// after a template fix without asking the phone for anything; matching moves money and
/// happens once. Keeping them apart means a wording change at the operator is a row in a
/// table and a replay, not an incident.
///
/// Reports what it did rather than logging it. The host decides what is worth writing
/// down and at what level, and this class stays runnable in a test with nothing around it.
/// </summary>
public sealed class PaymentPipeline(
    IPipelineStore store,
    IPaymentMatcher matcher,
    IMessageParser parser)
{
    public async Task<PipelineRunResult> RunOnceAsync(
        int batchSize = 50, CancellationToken ct = default)
    {
        var batch = await store.ClaimUnparsedAsync(batchSize, ct).ConfigureAwait(false);
        var items = new List<PipelineItemResult>(batch.Count);

        foreach (var pending in batch)
        {
            try
            {
                items.Add(await ProcessAsync(pending, ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One message that throws used to take the whole batch with it, and every
                // row in that batch stayed in Claimed until the stale sweep let it go -
                // which reads, from the outside, as a pipeline that silently does nothing.
                // Now the failure lands on the one row that caused it, says so on the
                // dashboard, and the rest of the batch carries on.
                items.Add(await FailAsync(pending, ex, ct).ConfigureAwait(false));
            }
        }

        return new PipelineRunResult { Items = items };
    }

    private async Task<PipelineItemResult> FailAsync(
        PendingRawEvent pending, Exception ex, CancellationToken ct)
    {
        var reason = Describe(ex);

        try
        {
            await store.MarkStateAsync(
                    pending.RawEventId, RawEventState.Unparseable, reason, ct)
                .ConfigureAwait(false);
        }
        catch (Exception markFailed)
        {
            // The store itself is unwell. The row stays in Claimed and the stale-claim
            // sweep brings it back; what matters is that the reason reaches the log.
            return new PipelineItemResult
            {
                RawEventId = pending.RawEventId,
                State = RawEventState.Claimed,
                Kind = MessageKind.Unknown,
                Error = $"{reason} (and marking it failed: {markFailed.Message})",
            };
        }

        return new PipelineItemResult
        {
            RawEventId = pending.RawEventId,
            State = RawEventState.Unparseable,
            Kind = MessageKind.Unknown,
            Error = reason,
        };
    }

    /// <summary>
    /// The innermost exception first, then the wrapper that carried it.
    ///
    /// This order is not cosmetic. The outer exception is usually the one that says
    /// nothing - "An error occurred while saving the entity changes. See the inner
    /// exception for details." is EF's way of telling you to look somewhere you cannot
    /// reach from a log line - while the innermost one is the database saying exactly
    /// which constraint refused and why. The reason is also truncated to fit a column,
    /// and truncation cuts the end, so the useful half has to come first.
    /// </summary>
    private static string Describe(Exception ex)
    {
        var chain = new List<Exception>();

        for (var current = ex; current is not null && chain.Count < 6; current = current.InnerException)
        {
            chain.Add(current);
        }

        var root = chain[^1];
        var reason = $"{root.GetType().Name}: {root.Message}";

        return chain.Count == 1 ? reason : $"{reason} (via {chain[0].GetType().Name})";
    }

    private async Task<PipelineItemResult> ProcessAsync(PendingRawEvent pending, CancellationToken ct)
    {
        var parsed = parser.Parse(pending.SenderId, pending.Body);

        if (parsed.Kind == MessageKind.Unknown)
        {
            // Never discarded. A message nobody taught the parser about is the first sign
            // an operator changed its wording, and it belongs in front of a person.
            await store.MarkStateAsync(
                    pending.RawEventId,
                    RawEventState.Unparseable,
                    parsed.FailureReason ?? "No template matched this message.",
                    ct)
                .ConfigureAwait(false);

            return new PipelineItemResult
            {
                RawEventId = pending.RawEventId,
                State = RawEventState.Unparseable,
                Kind = MessageKind.Unknown,
                Reason = parsed.FailureReason,
            };
        }

        if (!parsed.Kind.IsCredit())
        {
            // Cash out, an OTP, a bill receipt, the hold that precedes a merchant payment.
            // Recognised so it can be thrown away.
            await store.MarkStateAsync(pending.RawEventId, RawEventState.Ignored, ct: ct)
                .ConfigureAwait(false);

            return new PipelineItemResult
            {
                RawEventId = pending.RawEventId,
                State = RawEventState.Ignored,
                Kind = parsed.Kind,
            };
        }

        var parsedTransactionId = await store.SaveParsedAsync(
                pending,
                PaymentMethod.Bkash,
                parsed.Amount!.Value,
                parsed.TrxId!,
                parsed.CounterpartyMsisdn,
                parsed.BalanceAfter,
                parsed.OccurredAt!.Value,
                parsed.Confidence,
                ct)
            .ConfigureAwait(false);

        await store.MarkStateAsync(pending.RawEventId, RawEventState.Parsed, ct: ct)
            .ConfigureAwait(false);

        if (parsedTransactionId is null)
        {
            // This provider transaction is already in the system. Nothing to settle twice.
            await store.MarkStateAsync(
                    pending.RawEventId,
                    RawEventState.Parsed,
                    $"AlreadyProcessed: transaction {parsed.TrxId} is already in the system.",
                    ct)
                .ConfigureAwait(false);

            return new PipelineItemResult
            {
                RawEventId = pending.RawEventId,
                State = RawEventState.Parsed,
                Kind = parsed.Kind,
                TrxId = parsed.TrxId,
                Amount = parsed.Amount,
                Match = MatchOutcome.AlreadyProcessed,
            };
        }

        var result = await matcher.MatchAsync(
                new IncomingPayment
                {
                    ParsedTransactionId = parsedTransactionId.Value,
                    WalletId = pending.WalletId,
                    Method = PaymentMethod.Bkash,
                    Amount = parsed.Amount.Value,
                    TrxId = parsed.TrxId!,
                    Confidence = parsed.Confidence,
                    OccurredAt = parsed.OccurredAt.Value,
                },
                ct)
            .ConfigureAwait(false);

        // A payment that arrived, read perfectly, and settled nothing is the hardest thing
        // in this system to explain from the outside: the row says Parsed and the invoice
        // says unpaid, and nothing on the screen connects the two. The matcher already
        // knows why - the window had closed, no session expected that amount, the id was
        // seen before - so that sentence goes on the row beside the message rather than
        // only into a log nobody is watching.
        if (result.Outcome is not (MatchOutcome.Matched or MatchOutcome.Partial))
        {
            var why = string.IsNullOrWhiteSpace(result.Reason)
                ? result.Outcome.ToString()
                : $"{result.Outcome}: {result.Reason}";

            await store.MarkStateAsync(pending.RawEventId, RawEventState.Parsed, why, ct)
                .ConfigureAwait(false);
        }

        return new PipelineItemResult
        {
            RawEventId = pending.RawEventId,
            State = RawEventState.Parsed,
            Kind = parsed.Kind,
            TrxId = parsed.TrxId,
            Amount = parsed.Amount,
            Match = result.Outcome,
            InvoiceId = result.InvoiceId,
            Strategy = result.Strategy,
            Reason = result.Reason,
        };
    }
}
