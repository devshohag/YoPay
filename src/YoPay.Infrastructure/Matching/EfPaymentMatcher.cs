using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YoPay.Application.Matching;
using YoPay.Contracts.Webhooks;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Domain.Invoicing;
using YoPay.Infrastructure.Persistence;

namespace YoPay.Infrastructure.Matching;

/// <summary>
/// The transactional half of matching.
///
/// Everything below happens inside one database transaction holding a lock on the wallet:
/// the duplicate check, the candidate lookup, the decision, the match row, the invoice
/// update and the webhook that announces it. That is not tidiness. Split any of it out and
/// the failure is an invoice marked paid whose webhook never fires, or a webhook for a
/// match that rolled back - and a merchant who shipped an order for money that is not
/// there.
///
/// Correctness rests on three things in the database, in this order:
///
///   1. pg_advisory_xact_lock on the wallet, so two workers never evaluate the same wallet
///      at once. Transaction scoped, so it is released by commit or rollback and cannot be
///      left behind or expire early - which is exactly what a cache-based lock with a TTL
///      could do.
///   2. The partial unique index on (wallet_id, expected_amount), which is what makes an
///      incoming amount unambiguous.
///   3. The unique indexes on payment_matches. They are the last word: whatever happens
///      upstream, the database permits one match per invoice and one per transaction, and
///      the loser of a race gets an error rather than a second settlement.
/// </summary>
public sealed class EfPaymentMatcher(YoPayDbContext db) : IPaymentMatcher
{
    public async Task<MatchWorkResult> MatchAsync(
        IncomingPayment payment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payment);

        // The connection retries on a transient failure (EnableRetryOnFailure), and a
        // retrying execution strategy refuses a transaction opened behind its back: it
        // cannot know where to resume from. So the whole transaction is handed to the
        // strategy as one retriable unit instead.
        //
        // Without this, every single match threw before it began - which is exactly how
        // messages ended up parked in Claimed with nothing in the logs to explain it.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy
            .ExecuteAsync(() => MatchInTransactionAsync(payment, ct))
            .ConfigureAwait(false);
    }

    private async Task<MatchWorkResult> MatchInTransactionAsync(
        IncomingPayment payment, CancellationToken ct)
    {
        // A retry starts this method again from the top. Anything the failed attempt left
        // in the change tracker would be inserted a second time, so it goes first.
        db.ChangeTracker.Clear();

        await using var transaction = await db.Database
            .BeginTransactionAsync(ct)
            .ConfigureAwait(false);

        // Held until this transaction ends, no sooner and no later.
        await db.Database
            .ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({payment.WalletId.ToString()}, 0))", ct)
            .ConfigureAwait(false);

        var alreadyMatched = await db.PaymentMatches
            .AnyAsync(m => m.ParsedTransactionId == payment.ParsedTransactionId, ct)
            .ConfigureAwait(false);

        var trxHash = HashTrxId(payment.TrxId);

        var seenBefore = await db.FraudSignals
            .AnyAsync(f => f.Method == payment.Method && f.TrxIdHash == trxHash, ct)
            .ConfigureAwait(false);

        var claimed = await FindClaimedAsync(payment, ct).ConfigureAwait(false);
        var byAmount = await FindByAmountAsync(payment, ct).ConfigureAwait(false);

        var decision = MatchRules.Decide(
            payment, claimed, byAmount, alreadyMatched, seenBefore, AmountTolerance.Exact);

        if (decision.Outcome is not (MatchOutcome.Matched or MatchOutcome.Partial))
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return new MatchWorkResult
            {
                Outcome = decision.Outcome,
                Reason = decision.Reason,
            };
        }

        await ApplyAsync(payment, decision, trxHash, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new MatchWorkResult
        {
            Outcome = decision.Outcome,
            InvoiceId = decision.InvoiceId,
            Strategy = decision.Strategy,
            Reason = decision.Reason,
        };
    }

    private async Task<IReadOnlyList<MatchCandidate>> FindClaimedAsync(
        IncomingPayment payment, CancellationToken ct) =>
        await (from claim in db.PaymentClaims
               join invoice in db.Invoices on claim.InvoiceId equals invoice.Id
               join session in db.PaymentSessions on invoice.Id equals session.InvoiceId
               where claim.NormalisedTrxId == payment.TrxId
                     && claim.State == ClaimState.Pending
                     && session.WalletId == payment.WalletId
                     && session.State == SessionState.Open
               select new MatchCandidate
               {
                   SessionId = session.Id,
                   InvoiceId = invoice.Id,
                   ExpectedAmount = session.ExpectedAmount,
                   OpenedAt = session.OpenedAt,
                   GraceUntil = invoice.GraceUntil,
                   InvoiceStatus = invoice.Status,
                   ClaimId = claim.Id,
               })
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<MatchCandidate>> FindByAmountAsync(
        IncomingPayment payment, CancellationToken ct) =>
        await (from session in db.PaymentSessions
               join invoice in db.Invoices on session.InvoiceId equals invoice.Id
               where session.WalletId == payment.WalletId
                     && session.State == SessionState.Open
                     && session.ExpectedAmount == payment.Amount
               select new MatchCandidate
               {
                   SessionId = session.Id,
                   InvoiceId = invoice.Id,
                   ExpectedAmount = session.ExpectedAmount,
                   OpenedAt = session.OpenedAt,
                   GraceUntil = invoice.GraceUntil,
                   InvoiceStatus = invoice.Status,
               })
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private async Task ApplyAsync(
        IncomingPayment payment, MatchDecision decision, string trxHash, CancellationToken ct)
    {
        var invoice = await db.Invoices
            .FirstAsync(i => i.Id == decision.InvoiceId, ct)
            .ConfigureAwait(false);

        var status = decision.Outcome == MatchOutcome.Matched
            ? InvoiceStatus.Paid
            : InvoiceStatus.Partial;

        // Same boundary rule as the parsed transaction: PaidAt lands in a timestamptz, and
        // Npgsql only writes DateTimeOffset values whose offset is zero.
        InvoiceStateMachine.Transition(invoice, status, payment.OccurredAt.ToUniversalTime());

        db.PaymentMatches.Add(new PaymentMatch
        {
            InvoiceId = invoice.Id,
            ParsedTransactionId = payment.ParsedTransactionId,
            PaymentSessionId = decision.PaymentSessionId,
            Strategy = decision.Strategy!.Value,
            MatchedAt = DateTimeOffset.UtcNow,
        });

        db.FraudSignals.Add(new FraudSignal
        {
            Method = payment.Method,
            TrxIdHash = trxHash,
            Reason = FraudSignalReason.DuplicateTrxId,
            FirstSeenMerchantId = invoice.MerchantId,
        });

        if (decision.PaymentSessionId is { } sessionId)
        {
            await db.PaymentSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, SessionState.Matched), ct)
                .ConfigureAwait(false);
        }

        if (decision.ClaimId is { } claimId)
        {
            await db.PaymentClaims
                .Where(c => c.Id == claimId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, ClaimState.Matched), ct)
                .ConfigureAwait(false);
        }

        // Written here, in the same transaction, so the notification cannot exist without
        // the match or the match without the notification. T9 delivers it.
        db.OutboxMessages.Add(new OutboxMessage
        {
            Type = status == InvoiceStatus.Paid
                ? WebhookEvents.PaymentPaid
                : WebhookEvents.PaymentPartial,
            PayloadJson = JsonSerializer.Serialize(new PaymentWebhookPayload
            {
                Event = status == InvoiceStatus.Paid
                    ? WebhookEvents.PaymentPaid
                    : WebhookEvents.PaymentPartial,
                InvoiceId = invoice.Id,
                OrderRef = invoice.OrderRef,
                Status = invoice.Status,
                Amount = invoice.Amount,
                ReceivedAmount = payment.Amount,
                TrxId = payment.TrxId,
                PaidAt = invoice.PaidAt,
                SentAt = DateTimeOffset.UtcNow,
                MetadataJson = invoice.MetadataJson,
            }),
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Hashed because this table is the basis of the cross-merchant duplicate signal, and
    /// one merchant's transaction identifiers are not another's to read.
    /// </summary>
    private static string HashTrxId(string trxId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trxId.ToUpperInvariant())));
}
