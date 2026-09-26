using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YoPay.Application.Security;

/// <summary>
/// Builds and checks the HMAC over a request.
///
/// The canonical string covers the method, the path and query, the timestamp, the nonce
/// and a hash of the body. Every one of those is there because leaving it out allows a
/// specific attack: without the method, a signed GET replays as a DELETE; without the
/// path, a signature for one invoice authorises another; without the body hash, the
/// amount can be edited in flight.
///
/// The body is hashed rather than concatenated so a large upload does not have to be
/// held in memory twice, and so the canonical string stays a fixed, readable shape that
/// an SDK author can reproduce without ambiguity.
/// </summary>
public static class RequestSignature
{
    /// <summary>
    /// How far apart the two clocks may be. Five minutes is generous for a phone or a
    /// shared host that has never been near NTP, and short enough that a captured
    /// request is not useful for long. The nonce, not this window, is what actually
    /// stops replay - inside five minutes the same signed request would otherwise work
    /// as many times as it is sent.
    /// </summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);

    public static string Canonicalise(
        string method,
        string pathAndQuery,
        DateTimeOffset timestamp,
        string nonce,
        string body)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(pathAndQuery);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(body);

        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

        return string.Join('\n',
            method.ToUpperInvariant(),
            pathAndQuery,
            timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            nonce,
            bodyHash);
    }

    public static string Compute(string secret, string canonical)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(canonical);

        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(canonical);

        return Convert.ToHexString(HMACSHA256.HashData(key, data));
    }

    public static string Sign(string secret, SignedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Compute(secret, Canonicalise(
            request.Method, request.PathAndQuery, request.Timestamp, request.Nonce, request.Body));
    }

    /// <summary>
    /// Checks the clock and the signature. Says nothing about the nonce - see
    /// <see cref="SignatureVerifier"/> for why the order of those two steps matters.
    /// </summary>
    public static SignatureResult Verify(string secret, SignedRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Nonce) ||
            string.IsNullOrWhiteSpace(request.Signature) ||
            string.IsNullOrWhiteSpace(request.KeyId))
        {
            return SignatureResult.Fail(SignatureFailure.MalformedHeaders);
        }

        var drift = now - request.Timestamp;
        if (drift > ClockTolerance || drift < -ClockTolerance)
        {
            return SignatureResult.Fail(SignatureFailure.Expired);
        }

        var expected = Sign(secret, request);

        // Compared without regard to case. The signature is hex, where case carries no
        // information at all, and half the languages an SDK might be written in produce
        // lower case by default while .NET produces upper. Rejecting a correct signature
        // over that is a night of debugging for an integrator and buys nothing.
        var match = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected.ToUpperInvariant()),
            Encoding.UTF8.GetBytes(request.Signature.ToUpperInvariant()));

        return match ? SignatureResult.Ok : SignatureResult.Fail(SignatureFailure.Invalid);
    }
}
