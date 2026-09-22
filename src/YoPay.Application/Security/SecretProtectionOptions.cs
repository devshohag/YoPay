namespace YoPay.Application.Security;

/// <summary>
/// The key ring.
///
/// Plural keys, not one, because rotation is the whole point. A protector with a single
/// master key can encrypt and decrypt, but it cannot answer "which key was this written
/// with", so rotating means re-encrypting every row at once and hoping nothing was
/// missed. Keeping the retired keys readable turns that into a background sweep that can
/// be interrupted and resumed.
///
/// Keys are 32 raw bytes, base64 encoded. Generate one with:
///   openssl rand -base64 32
/// They belong in a Docker secret or the environment, never in appsettings.json.
/// </summary>
public sealed record SecretProtectionOptions
{
    /// <summary>The key new ciphertext is written with. Must be present in Keys.</summary>
    public required string ActiveKeyId { get; init; }

    /// <summary>Every key still needed to read existing data, including the active one.</summary>
    public required IReadOnlyDictionary<string, string> Keys { get; init; }

    /// <summary>
    /// Fails loudly at startup rather than at the first decrypt. A misconfigured key ring
    /// that starts cleanly is a service that looks healthy until the moment a merchant
    /// sends their first signed request.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ActiveKeyId))
        {
            throw new InvalidOperationException("Security:ActiveKeyId is not configured.");
        }

        if (Keys is null || Keys.Count == 0)
        {
            throw new InvalidOperationException("Security:Keys is empty.");
        }

        if (!Keys.ContainsKey(ActiveKeyId))
        {
            throw new InvalidOperationException(
                $"Security:ActiveKeyId '{ActiveKeyId}' has no matching entry in Security:Keys.");
        }

        foreach (var (id, material) in Keys)
        {
            if (id.Contains('.', StringComparison.Ordinal))
            {
                // The id is a field in the ciphertext envelope, which is dot separated.
                throw new InvalidOperationException($"Key id '{id}' must not contain a dot.");
            }

            if (!TryDecodeKey(material, out _))
            {
                throw new InvalidOperationException(
                    $"Key '{id}' must be exactly 32 bytes, base64 encoded. " +
                    "Generate one with: openssl rand -base64 32");
            }
        }
    }

    internal static bool TryDecodeKey(string material, out byte[] key)
    {
        key = [];

        try
        {
            var decoded = Convert.FromBase64String(material);
            if (decoded.Length != 32)
            {
                return false;
            }

            key = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
