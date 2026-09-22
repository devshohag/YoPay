using YoPay.Domain.Entities;
using YoPay.Domain.Enums;

namespace YoPay.Application.Invoicing;

/// <summary>
/// The narrow slice of persistence invoice creation needs. Narrow on purpose: a port
/// that exposes a queryable pulls the database's shape back into the application layer
/// and the separation stops meaning anything.
/// </summary>
public interface IInvoiceStore
{
    Task<Invoice?> FindByOrderRefAsync(Guid merchantId, string orderRef, CancellationToken ct = default);

    Task<Invoice?> FindByIdAsync(Guid merchantId, Guid invoiceId, CancellationToken ct = default);

    /// <summary>The named wallet, or the merchant's active wallet for that method.</summary>
    Task<Wallet?> FindWalletAsync(
        Guid merchantId, Guid? walletId, PaymentMethod method, CancellationToken ct = default);

    /// <summary>Amounts already spoken for by open sessions on this wallet.</summary>
    Task<IReadOnlyCollection<decimal>> OpenSessionAmountsAsync(
        Guid walletId, CancellationToken ct = default);

    /// <summary>Saves the invoice and its first session together, or neither.</summary>
    Task SaveAsync(Invoice invoice, PaymentSession session, CancellationToken ct = default);
}
