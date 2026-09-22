namespace YoPay.Application.Security;

/// <summary>
/// The wire format for an encrypted value: <c>v1.{keyId}.{base64(nonce|ciphertext|tag)}</c>
///
/// The key id travels in the clear, which is safe - it names a key, it is not one - and
/// it is what makes rotation possible at all. The version prefix costs four characters
/// and means a future format change does not require guessing what old rows contain.
/// </summary>
public readonly record struct SecretEnvelope(string Version, string KeyId, byte[] Payload)
{
    public const string CurrentVersion = "v1";

    public override string ToString() =>
        $"{Version}.{KeyId}.{Convert.ToBase64String(Payload)}";

    public static SecretEnvelope Create(string keyId, byte[] payload) =>
        new(CurrentVersion, keyId, payload);

    public static bool TryParse(string? value, out SecretEnvelope envelope)
    {
        envelope = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.', 3);
        if (parts.Length != 3 || parts[0] != CurrentVersion ||
            parts[1].Length == 0 || parts[2].Length == 0)
        {
            return false;
        }

        try
        {
            envelope = new SecretEnvelope(parts[0], parts[1], Convert.FromBase64String(parts[2]));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
