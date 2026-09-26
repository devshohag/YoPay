using Microsoft.EntityFrameworkCore;
using YoPay.Application.Invoicing;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;

namespace YoPay.Infrastructure.Persistence;

public sealed class EfInvoiceStore(YoPayDbContext db) : IInvoiceStore
{
    public Task<Invoice?> FindByOrderRefAsync(
        Guid merchantId, string orderRef, CancellationToken ct = default) =>
        db.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.MerchantId == merchantId && i.OrderRef == orderRef, ct);

    public Task<Invoice?> FindByIdAsync(
        Guid merchantId, Guid invoiceId, CancellationToken ct = default) =>
        db.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.MerchantId == merchantId && i.Id == invoiceId, ct);

    public Task<Wallet?> FindWalletAsync(
        Guid merchantId, Guid? walletId, PaymentMethod method, CancellationToken ct = default)
    {
        var query = db.Wallets
            .AsNoTracking()
            .Where(w => w.MerchantId == merchantId && w.IsActive);

        query = walletId is { } id
            ? query.Where(w => w.Id == id)
            : query.Where(w => w.Method == method);

        // Approved business accounts first: when a merchant has both kinds configured,
        // the sustainable rail is the one that should carry the traffic.
        return query
            .OrderBy(w => w.AccountType)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyCollection<decimal>> OpenSessionAmountsAsync(
        Guid walletId, CancellationToken ct = default) =>
        await db.PaymentSessions
            .AsNoTracking()
            .Where(s => s.WalletId == walletId && s.State == SessionState.Open)
            .Select(s => s.ExpectedAmount)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public Task<CheckoutView?> FindCheckoutAsync(Guid invoiceId, CancellationToken ct = default) =>
        db.Invoices
            .AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Join(db.Wallets, i => i.WalletId, w => w.Id, (i, w) => new { i, w })
            .Join(db.Merchants, x => x.i.MerchantId, m => m.Id, (x, m) => new CheckoutView
            {
                InvoiceId = x.i.Id,
                MerchantName = m.Name,
                OrderRef = x.i.OrderRef,
                Amount = x.i.Amount,
                ChargedAmount = x.i.ChargedAmount,
                Currency = x.i.Currency,
                Method = x.w.Method,
                PayToNumber = x.w.Number,
                AccountType = x.w.AccountType,
                Status = x.i.Status,
                ExpiresAt = x.i.ExpiresAt,
                GraceUntil = x.i.GraceUntil,
                RedirectUrl = x.i.RedirectUrl,
            })
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// One transaction. An invoice without its session is an invoice nothing can ever
    /// match, and a session without its invoice violates the foreign key - so neither is
    /// allowed to exist alone even for a moment.
    /// </summary>
    public async Task SaveAsync(Invoice invoice, PaymentSession session, CancellationToken ct = default)
    {
        db.Invoices.Add(invoice);
        db.PaymentSessions.Add(session);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task CancelAsync(Invoice invoice, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        // The execution strategy owns the transaction: the connection retries on a
        // transient failure and refuses a transaction opened behind its back.
        await db.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database
                    .BeginTransactionAsync(ct)
                    .ConfigureAwait(false);

                await db.Invoices
                    .Where(i => i.Id == invoice.Id)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(i => i.Status, invoice.Status), ct)
                    .ConfigureAwait(false);

                // Freeing the reserved amount is the point of cancelling, not a tidy-up
                // afterwards: while this session is open no other invoice on this wallet
                // may expect the same amount.
                await db.PaymentSessions
                    .Where(s => s.InvoiceId == invoice.Id && s.State == SessionState.Open)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.State, SessionState.Cancelled), ct)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);
            })
            .ConfigureAwait(false);
    }
}
