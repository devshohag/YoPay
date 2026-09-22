using System.Globalization;
using System.Text;
using YoPay.Application.Abstractions;
using YoPay.Application.Devices;
using YoPay.Application.Security;

namespace YoPay.Ingest.Api.Auth;

/// <summary>
/// Authenticates a handset from its signature.
///
/// Same canonical string as the merchant API, so there is one format to document and one
/// to get right, but a different key type: the merchant holds a shared secret, the device
/// holds a private key the server has never seen. That asymmetry matters - a stolen
/// database gives an attacker every merchant's signing secret, and not one device's.
///
/// Pairing is the one route that runs unauthenticated, because a handset that has not
/// paired yet has nothing to sign with.
/// </summary>
public sealed class DeviceSignedRequestMiddleware(
    RequestDelegate next,
    ILogger<DeviceSignedRequestMiddleware> log)
{
    public const string DeviceHeader = "X-YoPay-Device";

    private const int MaxBodyBytes = 512 * 1024;

    public async Task InvokeAsync(
        HttpContext context,
        IDeviceStore devices,
        INonceStore nonces,
        IClock clock,
        DeviceContext deviceContext)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Request.Path;

        if (!path.StartsWithSegments("/v1/ingest") || path.StartsWithSegments("/v1/ingest/pair"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // A batch of messages is bigger than a merchant API call but still small. The cap
        // runs before the body is read, because this is ahead of authentication.
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            await Problem(context, StatusCodes.Status413PayloadTooLarge, "payload_too_large")
                .ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(context.Request.Headers[DeviceHeader].ToString(), out var deviceId) ||
            !long.TryParse(
                context.Request.Headers[SignedRequest.TimestampHeader].ToString(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            await Problem(context, StatusCodes.Status401Unauthorized, "malformed_headers")
                .ConfigureAwait(false);
            return;
        }

        var nonce = context.Request.Headers[SignedRequest.NonceHeader].ToString();
        var signature = context.Request.Headers[SignedRequest.SignatureHeader].ToString();

        if (nonce.Length == 0 || signature.Length == 0)
        {
            await Problem(context, StatusCodes.Status401Unauthorized, "malformed_headers")
                .ConfigureAwait(false);
            return;
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unix);
        var drift = clock.UtcNow - timestamp;

        if (drift > RequestSignature.ClockTolerance || drift < -RequestSignature.ClockTolerance)
        {
            await Problem(context, StatusCodes.Status401Unauthorized, "timestamp_out_of_window")
                .ConfigureAwait(false);
            return;
        }

        var device = await devices.FindIdentityAsync(deviceId, context.RequestAborted)
            .ConfigureAwait(false);

        if (device is null || !device.IsActive)
        {
            await Problem(context, StatusCodes.Status401Unauthorized, "unauthorized")
                .ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);

        var canonical = RequestSignature.Canonicalise(
            context.Request.Method,
            context.Request.Path + context.Request.QueryString,
            timestamp,
            nonce,
            body);

        if (!DeviceSignature.Verify(device.PublicKey, canonical, signature))
        {
            log.LogInformation("Rejected ingest from device {DeviceId}: bad signature", deviceId);

            await Problem(context, StatusCodes.Status401Unauthorized, "unauthorized")
                .ConfigureAwait(false);
            return;
        }

        // Signature first, nonce second - same reasoning as the merchant API: burning a
        // nonce for an unsigned request lets anyone reachable fill the table.
        var fresh = await nonces
            .TryConsumeAsync(
                deviceId.ToString("N"),
                nonce,
                timestamp + RequestSignature.ClockTolerance,
                context.RequestAborted)
            .ConfigureAwait(false);

        if (!fresh)
        {
            await Problem(context, StatusCodes.Status401Unauthorized, "nonce_already_used")
                .ConfigureAwait(false);
            return;
        }

        deviceContext.Set(device);

        await next(context).ConfigureAwait(false);
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();

        using var reader = new StreamReader(
            request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        request.Body.Position = 0;

        return body;
    }

    private static async Task Problem(HttpContext context, int status, string reason)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new { status, reason }).ConfigureAwait(false);
    }
}
