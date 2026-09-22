using YoPay.Application.Invoicing;
using YoPay.Domain.Entities;

namespace YoPay.UnitTests;

internal sealed class FakeClaimStore : IClaimStore
{
    public List<PaymentClaim> Claims { get; } = [];

    public Task<int> CountForInvoiceAsync(Guid invoiceId, CancellationToken ct = default) =>
        Task.FromResult(Claims.Count(c => c.InvoiceId == invoiceId));

    public Task<bool> ExistsAsync(Guid invoiceId, string normalisedTrxId, CancellationToken ct = default) =>
        Task.FromResult(Claims.Exists(c => c.InvoiceId == invoiceId && c.NormalisedTrxId == normalisedTrxId));

    public Task AddAsync(PaymentClaim claim, CancellationToken ct = default)
    {
        Claims.Add(claim);
        return Task.CompletedTask;
    }
}
