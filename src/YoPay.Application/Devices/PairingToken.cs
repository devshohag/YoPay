using System.Security.Cryptography;
using System.Text;

namespace YoPay.Application.Devices;

/// <summary>
/// Generates and checks the pairing code.
///
/// Grouped into blocks of four because a merchant may have to read it aloud down a phone
/// line to whoever is holding the handset, and the alphabet drops the characters that get
/// misheard and misread.
/// </summary>
public static class PairingToken
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRTUVWXYZ2346789";
    private const int Blocks = 3;
    private const int BlockSize = 4;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static string New()
    {
        var parts = new string[Blocks];

        for (var b = 0; b < Blocks; b++)
        {
            var chars = new char[BlockSize];
            for (var i = 0; i < BlockSize; i++)
            {
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            parts[b] = new string(chars);
        }

        return string.Join('-', parts);
    }

    /// <summary>Accepts it typed with or without the dashes, in any case.</summary>
    public static string Normalise(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        return new string([.. token.ToUpperInvariant().Where(char.IsLetterOrDigit)]);
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalise(token))));
}
