using YoPay.Domain.Common;

namespace YoPay.Domain.Entities;

/// <summary>
/// One API key pair for a merchant.
///
/// The API key is stored as a hash because verification only ever needs a comparison.
/// The HMAC secret cannot be hashed: signing and verifying both need the original
/// value, so it is held encrypted at rest and decrypted in memory for the duration of
/// a request. Storing a hash here would make request signing impossible.
///
/// Rotation issues a new row with KeyVersion + 1 while the previous row stays valid
/// until RevokedAt, so a merchant can roll keys without downtime.
/// </summary>
public class ApiCredential : MerchantEntity
{
    /// <summary>Public identifier sent with every request. Not a secret.</summary>
    public string KeyId { get; set; } = null!;

    public string ApiKeyHash { get; set; } = null!;

    /// <summary>Encrypted with the data protection key ring - never a hash, never plaintext.</summary>
    public string HmacSecretEncrypted { get; set; } = null!;

    public int KeyVersion { get; set; } = 1;
    public string? Label { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }

    public Merchant Merchant { get; set; } = null!;

    public bool IsUsable(DateTimeOffset asOf) => RevokedAt is null || RevokedAt > asOf;
}
