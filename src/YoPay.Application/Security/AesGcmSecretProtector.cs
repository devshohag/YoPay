using System.Security.Cryptography;
using System.Text;
using YoPay.Application.Abstractions;

namespace YoPay.Application.Security;

/// <summary>
/// AES-256-GCM over the key ring.
///
/// Two details are deliberate:
///
/// - The key id is passed as associated data, not merely written in the header. Anyone
///   can edit the header of a stored string; if the id were only a label, an attacker
///   with write access to the row could point a ciphertext at a different key and watch
///   what happens. As associated data it is authenticated, so tampering fails the tag
///   check instead of producing a decryption attempt.
///
/// - Decryption looks the key up by id and fails cleanly when the ring no longer carries
///   it. That failure is a real operational event - a key was retired before everything
///   written with it was re-encrypted - and it should read as exactly that in the logs,
///   not as corrupt data.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly Dictionary<string, byte[]> _keys;

    public AesGcmSecretProtector(SecretProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        ActiveKeyId = options.ActiveKeyId;
        _keys = [];

        foreach (var (id, material) in options.Keys)
        {
            SecretProtectionOptions.TryDecodeKey(material, out var key);
            _keys[id] = key;
        }
    }

    public string ActiveKeyId { get; }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var key = _keys[ActiveKeyId];
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);

        var payload = new byte[NonceSize + plainBytes.Length + TagSize];
        var nonce = payload.AsSpan(0, NonceSize);
        var cipher = payload.AsSpan(NonceSize, plainBytes.Length);
        var tag = payload.AsSpan(NonceSize + plainBytes.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag, AssociatedData(ActiveKeyId));

        return SecretEnvelope.Create(ActiveKeyId, payload).ToString();
    }

    public string Unprotect(string ciphertext)
    {
        if (!SecretEnvelope.TryParse(ciphertext, out var envelope))
        {
            throw new CryptographicException("The value is not a recognised secret envelope.");
        }

        if (!_keys.TryGetValue(envelope.KeyId, out var key))
        {
            throw new CryptographicException(
                $"Key '{envelope.KeyId}' is not on the ring. It was retired before every " +
                "value written with it had been re-encrypted.");
        }

        var payload = envelope.Payload;
        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The envelope payload is too short to be valid.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var cipher = payload.AsSpan(NonceSize, payload.Length - NonceSize - TagSize);
        var tag = payload.AsSpan(payload.Length - TagSize, TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, AssociatedData(envelope.KeyId));

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>True when the value should be rewritten by the rotation sweep.</summary>
    public bool NeedsRotation(string ciphertext) =>
        !SecretEnvelope.TryParse(ciphertext, out var envelope) ||
        !string.Equals(envelope.KeyId, ActiveKeyId, StringComparison.Ordinal);

    private static byte[] AssociatedData(string keyId) => Encoding.UTF8.GetBytes(keyId);
}
