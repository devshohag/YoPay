namespace YoPay.Application.Abstractions;

/// <summary>What a valid API key resolves to.</summary>
public sealed record MerchantCredential
{
    public required Guid MerchantId { get; init; }
    public required string KeyId { get; init; }

    /// <summary>
    /// Decrypted, and never logged, never returned in a response, never put on an
    /// exception message. It exists in memory for the length of one signature check.
    /// </summary>
    public required string HmacSecret { get; init; }
}

public interface ICredentialLookup
{
    /// <summary>
    /// Returns the credential for a key id, or null when the key is unknown, revoked, or
    /// belongs to a merchant who is no longer active. One null for all three: telling a
    /// caller which of those applies tells them which key ids exist.
    /// </summary>
    Task<MerchantCredential?> FindAsync(string keyId, CancellationToken ct = default);

    /// <summary>Fire-and-forget usage stamp. Throttled by the implementation.</summary>
    Task TouchAsync(string keyId, string? ip, CancellationToken ct = default);
}
