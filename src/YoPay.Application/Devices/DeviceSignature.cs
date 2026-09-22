using System.Security.Cryptography;
using System.Text;

namespace YoPay.Application.Devices;

/// <summary>
/// Verifies that a request really came from a paired handset.
///
/// ECDSA over P-256, not Ed25519. Ed25519 is the nicer algorithm, but the private key has
/// to live in the Android Keystore - where it is generated in hardware and cannot be
/// extracted even from a rooted phone - and Keystore support for Ed25519 is patchy across
/// the manufacturers this product has to run on, while P-256 is available everywhere and
/// hardware backed on most of them. A key that cannot leave the phone beats a slightly
/// better curve in software.
///
/// The public key arrives as base64 SubjectPublicKeyInfo, which is what both Android and
/// .NET export natively, so neither side has to hand-roll a key format.
/// </summary>
public static class DeviceSignature
{
    public static bool Verify(string publicKeyBase64, string canonical, string signatureBase64)
    {
        if (string.IsNullOrWhiteSpace(publicKeyBase64) ||
            string.IsNullOrWhiteSpace(signatureBase64) ||
            string.IsNullOrWhiteSpace(canonical))
        {
            return false;
        }

        byte[] publicKey;
        byte[] signature;

        try
        {
            publicKey = Convert.FromBase64String(publicKeyBase64);
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);

            return ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(canonical),
                signature,
                HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            // A malformed key or signature is a failed verification, not a server error.
            return false;
        }
    }

    /// <summary>Used by the simulator and the tests. The real device never gives up its
    /// private key, so nothing in production signs with this.</summary>
    public static string Sign(ECDsa privateKey, string canonical)
    {
        ArgumentNullException.ThrowIfNull(privateKey);

        return Convert.ToBase64String(privateKey.SignData(
            Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256));
    }
}
