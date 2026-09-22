namespace YoPay.Application.Abstractions;

/// <summary>
/// Encrypts values that must be read back later - the HMAC secret above all, which
/// cannot be hashed because signing needs the original.
///
/// The key id travels with the ciphertext. YoMail's protector takes a single master key
/// from configuration and writes bare ciphertext, which means there is no way to tell
/// which key encrypted a given value, and therefore no way to rotate one without
/// re-encrypting everything at once and hoping nothing was missed. Porting that as-is
/// would make the KeyVersion column on ApiCredential decorative.
///
/// Implementation lands in T3: AES-256-GCM, a key ring loaded from configuration or a
/// Docker secret, the active key id stamped into every new ciphertext, and old keys kept
/// readable until a re-encryption sweep finishes.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypts with the currently active key and stamps its id into the output.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts using the key id carried by the ciphertext.</summary>
    string Unprotect(string ciphertext);

    /// <summary>The key id a fresh Protect call would use. Used by the rotation sweep to
    /// find rows still holding an older key.</summary>
    string ActiveKeyId { get; }
}
