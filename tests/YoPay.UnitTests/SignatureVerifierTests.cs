using YoPay.Application.Security;

namespace YoPay.UnitTests;

public class SignatureVerifierTests
{
    private const string Secret = "merchant-hmac-secret";
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private static SignedRequest Signed(string nonce)
    {
        var request = new SignedRequest
        {
            KeyId = "key-1",
            Timestamp = Now,
            Nonce = nonce,
            Signature = "",
            Method = "POST",
            PathAndQuery = "/v1/payment/create",
            Body = "{\"amount\":500}",
        };

        return request with { Signature = RequestSignature.Sign(Secret, request) };
    }

    [Fact]
    public async Task A_valid_request_passes_once()
    {
        var verifier = new SignatureVerifier(new RecordingNonceStore(), new FixedClock(Now));

        Assert.True((await verifier.VerifyAsync(Secret, Signed("n-1"))).Succeeded);
    }

    [Fact]
    public async Task The_same_signed_request_sent_twice_is_refused_the_second_time()
    {
        // Inside the five minute window the signature stays valid, so without the nonce
        // the identical request would work as many times as it is sent.
        var verifier = new SignatureVerifier(new RecordingNonceStore(), new FixedClock(Now));
        var request = Signed("n-1");

        Assert.True((await verifier.VerifyAsync(Secret, request)).Succeeded);

        var second = await verifier.VerifyAsync(Secret, request);

        Assert.Equal(SignatureFailure.Replayed, second.Failure);
    }

    [Fact]
    public async Task A_bad_signature_never_reaches_the_nonce_store()
    {
        // The ordering rule, asserted rather than commented. If the nonce were consumed
        // first, anyone who could reach the endpoint could fill the table without holding
        // a key, and could burn a nonce a legitimate client was about to use.
        var nonces = new RecordingNonceStore();
        var verifier = new SignatureVerifier(nonces, new FixedClock(Now));

        var forged = Signed("n-1") with { Signature = new string('A', 64) };

        var result = await verifier.VerifyAsync(Secret, forged);

        Assert.Equal(SignatureFailure.Invalid, result.Failure);
        Assert.Equal(0, nonces.ConsumeAttempts);
    }

    [Fact]
    public async Task An_expired_request_never_reaches_the_nonce_store_either()
    {
        var nonces = new RecordingNonceStore();
        var verifier = new SignatureVerifier(nonces, new FixedClock(Now + TimeSpan.FromHours(1)));

        var result = await verifier.VerifyAsync(Secret, Signed("n-1"));

        Assert.Equal(SignatureFailure.Expired, result.Failure);
        Assert.Equal(0, nonces.ConsumeAttempts);
    }
}
