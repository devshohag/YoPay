using YoPay.Domain.Common;
using YoPay.Domain.Enums;

namespace YoPay.Domain.Entities;

/// <summary>A merchant's receiving wallet. Money lands here directly; YoPay only observes it.</summary>
public class Wallet : MerchantEntity
{
    public PaymentMethod Method { get; set; }

    /// <summary>The receiving number shown to customers on the checkout page.</summary>
    public string Number { get; set; } = null!;

    public WalletAccountType AccountType { get; set; } = WalletAccountType.PersonalRetail;
    public string? Label { get; set; }

    /// <summary>
    /// The limit the provider has stated for this account, as the merchant reported it.
    /// Used for warnings as the merchant approaches it. YoPay never splits load across
    /// wallets to work around a limit.
    /// </summary>
    public decimal? DeclaredMonthlyLimit { get; set; }

    public bool IsActive { get; set; } = true;

    public Merchant Merchant { get; set; } = null!;
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
