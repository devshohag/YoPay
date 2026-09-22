using YoPay.Domain.Common;

namespace YoPay.Domain.Entities;

/// <summary>A paying customer of YoPay. Owns wallets, devices, invoices and API credentials.</summary>
public class Merchant : Entity
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string ContactEmail { get; set; } = null!;
    public string? ContactMsisdn { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Wallet> Wallets { get; set; } = new List<Wallet>();
    public ICollection<ApiCredential> Credentials { get; set; } = new List<ApiCredential>();
}
