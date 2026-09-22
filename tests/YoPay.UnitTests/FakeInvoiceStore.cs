using YoPay.Application.Invoicing;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;

namespace YoPay.UnitTests;

internal sealed class FakeInvoiceStore : IInvoiceStore
{
    public List<Invoice> Invoices { get; } = [];
    public List<PaymentSession> Sessions { get; } = [];
    public List<Wallet> Wallets { get; } = [];
    public List<decimal> OpenAmounts { get; } = [];
    public int SaveCalls { get; private set; }
    public CheckoutView? Checkout { get; set; }

    public Task<CheckoutView?> FindCheckoutAsync(Guid invoiceId, CancellationToken ct = default) =>
        Task.FromResult(Checkout);

    public Task<Invoice?> FindByOrderRefAsync(
        Guid merchantId, string orderRef, CancellationToken ct = default) =>
        Task.FromResult(Invoices.Find(i => i.MerchantId == merchantId && i.OrderRef == orderRef));

    public Task<Invoice?> FindByIdAsync(Guid merchantId, Guid invoiceId, CancellationToken ct = default) =>
        Task.FromResult(Invoices.Find(i => i.MerchantId == merchantId && i.Id == invoiceId));

    public Task<Wallet?> FindWalletAsync(
        Guid merchantId, Guid? walletId, PaymentMethod method, CancellationToken ct = default) =>
        Task.FromResult(Wallets.Find(w =>
            w.MerchantId == merchantId &&
            (walletId is null ? w.Method == method : w.Id == walletId)));

    public Task<IReadOnlyCollection<decimal>> OpenSessionAmountsAsync(
        Guid walletId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyCollection<decimal>>(OpenAmounts);

    public Task SaveAsync(Invoice invoice, PaymentSession session, CancellationToken ct = default)
    {
        SaveCalls++;
        Invoices.Add(invoice);
        Sessions.Add(session);
        return Task.CompletedTask;
    }
}
