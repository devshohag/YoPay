using System.Security.Cryptography;

namespace YoPay.Application.Security;

/// <summary>
/// Issues and checks merchant API keys.
///
/// The key is 32 bytes from the system CSPRNG, not a password, so it is stored as a
/// plain SHA-256 hash rather than behind a slow KDF. Argon2 or PBKDF2 exist to make
/// guessing a human-chosen secret expensive; against 256 bits of entropy there is
/// nothing to guess, and putting a deliberately slow function on the authentication path
/// of every API call buys latency instead of security.
///
/// What does matter is that the comparison is constant time, so a wrong key costs
/// exactly what a right one does and the hash cannot be recovered a byte at a time.
/// </summary>
public static class ApiKey
{
    /// <summary>Lets a leaked key be recognised on sight, in a log or a screenshot.</summary>
    public const string Prefix = "yop_";

    public static (string KeyId, string Secret, string Hash) Issue()
    {
        var keyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var secret = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

        return (keyId, secret, Hash(secret));
    }

    public static string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
    }

    public static bool Matches(string presented, string storedHash)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var presentedHash = System.Text.Encoding.UTF8.GetBytes(Hash(presented));
        var expected = System.Text.Encoding.UTF8.GetBytes(storedHash);

        return CryptographicOperations.FixedTimeEquals(presentedHash, expected);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
