using YoPay.Domain.Entities;

namespace YoPay.Application.Invoicing;

public interface IClaimStore
{
    Task<int> CountForInvoiceAsync(Guid invoiceId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid invoiceId, string normalisedTrxId, CancellationToken ct = default);

    Task AddAsync(PaymentClaim claim, CancellationToken ct = default);
}
