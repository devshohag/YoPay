using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YoPay.Application.Webhooks;

/// <summary>
/// The proof that a webhook came from YoPay.
///
/// A merchant's endpoint is a public URL. Anyone who finds it can POST a JSON body that
/// says an invoice is paid, and a shop that trusts the body ships goods for nothing. The
/// signature is the only thing standing between a merchant and that, which is why the
/// documentation says verification is not optional and why the SDKs refuse to parse an
/// unverified payload at all.
///
/// The signed string is "{timestamp}.{body}", not the body alone. Signing the body alone
/// produces a token that stays valid forever: an attacker who captures one legitimate
/// "paid" delivery can replay it against the same endpoint next week and the signature
/// still checks out. Binding the timestamp into the MAC means a replay has to carry the
/// original timestamp, which the merchant rejects as stale.
///
/// Header: X-YoPay-Signature: t=1758600000,v1=9f86d081...
///
/// The version tag is not decoration either. It is the only way to move to a different
/// algorithm later without every existing integration breaking on the same day.
/// </summary>
public static class WebhookSignature
{
    public const string HeaderName = "X-YoPay-Signature";
    public const string EventHeaderName = "X-YoPay-Event";
    public const string DeliveryHeaderName = "X-YoPay-Delivery";

    /// <summary>
    /// How stale a delivery may be before a merchant should refuse it. Published so the
    /// SDKs use one number and the documentation is not guessing.
    /// </summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private const string Version = "v1";

    public static string Canonicalise(DateTimeOffset timestamp, string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var seconds = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        return $"{seconds}.{body}";
    }

    /// <summary>The header value, ready to send.</summary>
    public static string Sign(string secret, DateTimeOffset timestamp, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var mac = Compute(secret, Canonicalise(timestamp, body));
        var seconds = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        return $"t={seconds},{Version}={mac}";
    }

    /// <summary>
    /// What a merchant's server does with what it received. Lives here so the SDKs and
    /// the tests share one implementation with the signing side, rather than two
    /// descriptions of the same rule that drift apart.
    /// </summary>
    public static bool Verify(string secret, string header, string body, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) ||
            string.IsNullOrWhiteSpace(header) ||
            body is null)
        {
            return false;
        }

        if (!TryRead(header, out var timestamp, out var presented))
        {
            return false;
        }

        // Checked before the MAC, because an expired delivery is not worth a comparison -
        // and because a merchant who skips this check has a signature that never expires.
        if (now - timestamp > Tolerance || timestamp - now > Tolerance)
        {
            return false;
        }

        var expected = Compute(secret, Canonicalise(timestamp, body));

        // Constant time: a byte-by-byte comparison leaks how much of a forged MAC was
        // right, and that is enough to construct the rest one byte at a time. Case is
        // normalised first because hex case carries no information, and a merchant whose
        // language uppercases by default should not lose a night to it.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(presented.ToLowerInvariant()));
    }

    private static bool TryRead(string header, out DateTimeOffset timestamp, out string mac)
    {
        timestamp = default;
        mac = string.Empty;

        long? seconds = null;

        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator];
            var value = part[(separator + 1)..];

            if (key == "t" && long.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
            {
                seconds = parsed;
            }
            else if (key == Version)
            {
                mac = value;
            }
        }

        if (seconds is null || mac.Length == 0)
        {
            return false;
        }

        timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds.Value);
        return true;
    }

    private static string Compute(string secret, string canonical)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));

        return Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
