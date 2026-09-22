using System.Globalization;
using System.Text;
using YoPay.Application.Abstractions;
using YoPay.Application.Security;

namespace YoPay.Api.Auth;

/// <summary>
/// Authenticates every /v1 request from its signature.
///
/// The body has to be read here to hash it, and then handed intact to the endpoint, which
/// is what EnableBuffering is for. The size cap before that read is not politeness: this
/// runs before authentication, so without it an unauthenticated caller can make the
/// server buffer whatever they send.
///
/// Every failure answers 401 with the same shape and a machine-readable reason. Which
/// reason is safe to state varies - an unknown key and a bad signature are both just
/// "unauthorized" to the caller, because saying which one applies tells an attacker
/// whether a key id exists.
/// </summary>
public sealed class SignedRequestMiddleware(RequestDelegate next, ILogger<SignedRequestMiddleware> log)
{
    private const int MaxBodyBytes = 64 * 1024;

    public async Task InvokeAsync(
        HttpContext context,
        ICredentialLookup credentials,
        SignatureVerifier verifier,
        MerchantContext merchant)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments("/v1"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (context.Request.ContentLength > MaxBodyBytes)
        {
            await WriteProblem(context, StatusCodes.Status413PayloadTooLarge,
                "Request body too large", "payload_too_large").ConfigureAwait(false);
            return;
        }

        if (!TryReadHeaders(context.Request, out var keyId, out var timestamp, out var nonce, out var signature))
        {
            await Unauthorized(context, "malformed_headers").ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);

        var credential = await credentials.FindAsync(keyId, context.RequestAborted).ConfigureAwait(false);
        if (credential is null)
        {
            // Same answer and roughly the same work as a bad signature, so the response
            // does not reveal which key ids exist.
            await Unauthorized(context, "unauthorized").ConfigureAwait(false);
            return;
        }

        var request = new SignedRequest
        {
            KeyId = keyId,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = signature,
            Method = context.Request.Method,
            PathAndQuery = context.Request.Path + context.Request.QueryString,
            Body = body,
        };

        var result = await verifier
            .VerifyAsync(credential.HmacSecret, request, context.RequestAborted)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Expired and replayed are safe to name: a caller with a wrong clock or a
            // retry loop needs to know which, and neither leaks anything.
            var reason = result.Failure switch
            {
                SignatureFailure.Expired => "timestamp_out_of_window",
                SignatureFailure.Replayed => "nonce_already_used",
                _ => "unauthorized",
            };

            log.LogInformation(
                "Rejected {Method} {Path} for key {KeyId}: {Reason}",
                context.Request.Method, context.Request.Path, keyId, reason);

            await Unauthorized(context, reason).ConfigureAwait(false);
            return;
        }

        merchant.Set(credential.MerchantId, credential.KeyId);

        await credentials
            .TouchAsync(keyId, context.Connection.RemoteIpAddress?.ToString(), context.RequestAborted)
            .ConfigureAwait(false);

        await next(context).ConfigureAwait(false);
    }

    private static bool TryReadHeaders(
        HttpRequest request,
        out string keyId,
        out DateTimeOffset timestamp,
        out string nonce,
        out string signature)
    {
        keyId = request.Headers[SignedRequest.KeyHeader].ToString();
        nonce = request.Headers[SignedRequest.NonceHeader].ToString();
        signature = request.Headers[SignedRequest.SignatureHeader].ToString();
        timestamp = default;

        var raw = request.Headers[SignedRequest.TimestampHeader].ToString();

        if (keyId.Length == 0 || nonce.Length == 0 || signature.Length == 0 || raw.Length == 0)
        {
            return false;
        }

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            return false;
        }

        timestamp = DateTimeOffset.FromUnixTimeSeconds(unix);
        return true;
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

    private static Task Unauthorized(HttpContext context, string reason) =>
        WriteProblem(context, StatusCodes.Status401Unauthorized, "Unauthorized", reason);

    private static async Task WriteProblem(
        HttpContext context, int status, string title, string reason)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title,
            status,
            reason,
        }).ConfigureAwait(false);
    }
}
