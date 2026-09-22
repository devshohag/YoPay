using YoPay.Application.Abstractions;

namespace YoPay.Application.Security;

/// <summary>
/// The full check: clock, signature, then nonce.
///
/// That order is the point of this class existing at all.
///
/// Burning the nonce first looks tidier - one database round trip either way - but it
/// hands anyone who can reach the endpoint a way to fill the nonce table with garbage
/// without holding a single valid key. Worse, if an attacker can guess or observe a
/// nonce a legitimate client is about to use, consuming it first lets them cancel that
/// client's request. Checking the signature first means only a caller who already holds
/// the secret can write anything at all.
///
/// The cost is that a replayed valid request does the HMAC work twice. That is
/// microseconds, and it buys a property worth far more than microseconds.
/// </summary>
public sealed class SignatureVerifier(INonceStore nonces, IClock clock)
{
    public async Task<SignatureResult> VerifyAsync(
        string secret,
        SignedRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var signature = RequestSignature.Verify(secret, request, clock.UtcNow);
        if (!signature.Succeeded)
        {
            return signature;
        }

        var accepted = await nonces
            .TryConsumeAsync(
                request.KeyId,
                request.Nonce,
                request.Timestamp + RequestSignature.ClockTolerance,
                ct)
            .ConfigureAwait(false);

        return accepted
            ? SignatureResult.Ok
            : SignatureResult.Fail(SignatureFailure.Replayed);
    }
}
