using YoPay.Domain.Common;
using YoPay.Domain.Enums;

namespace YoPay.Domain.Entities;

/// <summary>
/// A time-boxed attempt to pay one invoice into one wallet.
///
/// ExpectedAmount is unique among the open sessions of a wallet, enforced by a partial
/// unique index in the database rather than by application checks - that index is what
/// makes an incoming amount unambiguous.
/// </summary>
public class PaymentSession : Entity
{
    public Guid InvoiceId { get; set; }
    public Guid WalletId { get; set; }

    public decimal ExpectedAmount { get; set; }

    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    public SessionState State { get; set; } = SessionState.Open;

    public Invoice Invoice { get; set; } = null!;
    public Wallet Wallet { get; set; } = null!;
}
