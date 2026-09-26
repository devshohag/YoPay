using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YoPay.Sdk;

/// <summary>
/// The two signatures in this system.
///
/// Written out here rather than shared with the server's own code, on purpose. A merchant
/// referencing YoPay's internals would be coupled to every refactor inside the platform,
/// and the platform would no longer be free to change them. This file is the contract,
/// and it is checked against signatures the server generated - see the test vectors in
/// the SDK's tests folder.
///
/// The two are deliberately different shapes, because they answer different questions:
///
/// - A REQUEST signature proves a call to YoPay came from this merchant. It covers the
///   method and path as well as the body, because without the method a signed GET replays
///   as a DELETE, and without the path a signature for one invoice authorises another. It
///   carries a nonce the server refuses to see twice.
///
/// - A WEBHOOK signature proves a delivery came from YoPay. There is no nonce: your server
///   has no shared store to remember one in, so replay is stopped by the timestamp inside
///   the MAC plus your own idempotency.
///
/// Hex case is not significant on either side.
/// </summary>
public static class YoPaySignature
{
    public const string KeyHeader = "X-YoPay-Key";
    public const string TimestampHeader = "X-YoPay-Timestamp";
    public const string NonceHeader = "X-YoPay-Nonce";
    public const string SignatureHeader = "X-YoPay-Signature";
    public const string EventHeader = "X-YoPay-Event";
    public const string DeliveryHeader = "X-YoPay-Delivery";

    /// <summary>How far apart the clocks may be.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static string CanonicaliseRequest(
        string method, string pathAndQuery, long unixSeconds, string nonce, string body)
    {
        if (method is null) throw new ArgumentNullException(nameof(method));
        if (pathAndQuery is null) throw new ArgumentNullException(nameof(pathAndQuery));
        if (nonce is null) throw new ArgumentNullException(nameof(nonce));
        if (body is null) throw new ArgumentNullException(nameof(body));

        string bodyHash;
        using (var sha = SHA256.Create())
        {
            bodyHash = ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(body)));
        }

        return string.Join("\n",
            method.ToUpperInvariant(),
            pathAndQuery,
            unixSeconds.ToString(CultureInfo.InvariantCulture),
            nonce,
            bodyHash.ToUpperInvariant());
    }

    public static string SignRequest(
        string secret, string method, string pathAndQuery, long unixSeconds, string nonce, string body) =>
        Hmac(secret, CanonicaliseRequest(method, pathAndQuery, unixSeconds, nonce, body));

    /// <summary>
    /// Checks the X-YoPay-Signature header on a delivery YoPay sent you.
    ///
    /// Pass the RAW body, exactly as it arrived. Deserialising and re-serialising changes
    /// the whitespace, and the MAC is over bytes - that is the single most common reason a
    /// correct-looking integration reports that signatures never verify. In ASP.NET Core
    /// that means enabling buffering and reading the stream yourself, not taking a bound
    /// model and writing it back out.
    /// </summary>
    public static bool VerifyWebhook(string secret, string header, string body, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header) || body is null)
        {
            return false;
        }

        long? seconds = null;
        string? presented = null;

        foreach (var piece in header.Split(','))
        {
            var part = piece.Trim();
            var separator = part.IndexOf('=');

            if (separator <= 0)
            {
                continue;
            }

            var key = part.Substring(0, separator);
            var value = part.Substring(separator + 1);

            if (key == "t" && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                seconds = parsed;
            }
            else if (key == "v1")
            {
                presented = value;
            }
        }

        if (seconds is null || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(seconds.Value);
        var at = now ?? DateTimeOffset.UtcNow;

        // Checked before the MAC. Skip it and the signature never expires, which turns any
        // captured "paid" delivery into a free order for whoever recorded it.
        if (at - sentAt > Tolerance || sentAt - at > Tolerance)
        {
            return false;
        }

        var expected = Hmac(secret, seconds.Value.ToString(CultureInfo.InvariantCulture) + "." + body);

        return FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(presented!.ToLowerInvariant()));
    }

    private static string Hmac(string secret, string canonical)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));

        return ToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);

        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Constant time. A short-circuiting comparison leaks how much of a forged MAC was
    /// right, which is enough to build the rest of it one byte at a time. Written out
    /// rather than taken from CryptographicOperations so this file also compiles for
    /// .NET Framework, where that class does not exist.
    /// </summary>
    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var difference = 0;

        for (var i = 0; i < left.Length; i++)
        {
            difference |= left[i] ^ right[i];
        }

        return difference == 0;
    }
}
