using Microsoft.EntityFrameworkCore;
using YoPay.Application.Invoicing;
using YoPay.Domain.Entities;

namespace YoPay.Infrastructure.Persistence;

public sealed class EfClaimStore(YoPayDbContext db) : IClaimStore
{
    public Task<int> CountForInvoiceAsync(Guid invoiceId, CancellationToken ct = default) =>
        db.PaymentClaims.CountAsync(c => c.InvoiceId == invoiceId, ct);

    public Task<bool> ExistsAsync(Guid invoiceId, string normalisedTrxId, CancellationToken ct = default) =>
        db.PaymentClaims.AnyAsync(
            c => c.InvoiceId == invoiceId && c.NormalisedTrxId == normalisedTrxId, ct);

    public async Task AddAsync(PaymentClaim claim, CancellationToken ct = default)
    {
        db.PaymentClaims.Add(claim);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
