using YoPay.Application.Security;

namespace YoPay.UnitTests;

public class RequestSignatureTests
{
    private const string Secret = "merchant-hmac-secret";
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private static SignedRequest Request(
        string method = "POST",
        string path = "/v1/payment/create",
        string body = "{\"amount\":500}",
        string nonce = "n-1",
        DateTimeOffset? timestamp = null)
    {
        var request = new SignedRequest
        {
            KeyId = "key-1",
            Timestamp = timestamp ?? Now,
            Nonce = nonce,
            Signature = "",
            Method = method,
            PathAndQuery = path,
            Body = body,
        };

        return request with { Signature = RequestSignature.Sign(Secret, request) };
    }

    [Fact]
    public void A_correctly_signed_request_verifies()
    {
        Assert.True(RequestSignature.Verify(Secret, Request(), Now).Succeeded);
    }

    [Theory]
    [InlineData("GET", "/v1/payment/create", "{\"amount\":500}")]
    [InlineData("POST", "/v1/payment/refund", "{\"amount\":500}")]
    [InlineData("POST", "/v1/payment/create", "{\"amount\":5}")]
    public void Changing_any_part_of_the_request_breaks_the_signature(
        string method, string path, string body)
    {
        // Each of these is an attack if the field is left out of the canonical string:
        // a signed GET replayed as a DELETE, a signature for one invoice reused on
        // another, an amount edited in flight.
        var signed = Request();
        var altered = signed with { Method = method, PathAndQuery = path, Body = body };

        Assert.Equal(
            SignatureFailure.Invalid,
            RequestSignature.Verify(Secret, altered, Now).Failure);
    }

    [Fact]
    public void A_request_signed_with_the_wrong_secret_is_rejected()
    {
        Assert.Equal(
            SignatureFailure.Invalid,
            RequestSignature.Verify("not-the-secret", Request(), Now).Failure);
    }

    [Fact]
    public void A_stale_request_is_rejected()
    {
        var old = Request(timestamp: Now - TimeSpan.FromMinutes(10));

        Assert.Equal(SignatureFailure.Expired, RequestSignature.Verify(Secret, old, Now).Failure);
    }

    [Fact]
    public void A_request_from_the_future_is_rejected_too()
    {
        // A clock that is wrong in the other direction is just as wrong, and accepting it
        // would let a caller mint signatures that stay valid for as long as they like.
        var ahead = Request(timestamp: Now + TimeSpan.FromMinutes(10));

        Assert.Equal(SignatureFailure.Expired, RequestSignature.Verify(Secret, ahead, Now).Failure);
    }

    [Fact]
    public void Modest_clock_drift_is_tolerated()
    {
        var skewed = Request(timestamp: Now - TimeSpan.FromMinutes(4));

        Assert.True(RequestSignature.Verify(Secret, skewed, Now).Succeeded);
    }

    [Fact]
    public void Missing_headers_are_reported_as_malformed_not_invalid()
    {
        var blank = Request() with { Nonce = "" };

        Assert.Equal(
            SignatureFailure.MalformedHeaders,
            RequestSignature.Verify(Secret, blank, Now).Failure);
    }

    [Fact]
    public void The_canonical_string_is_the_shape_the_sdks_must_reproduce()
    {
        var canonical = RequestSignature.Canonicalise(
            "post", "/v1/payment/create", Now, "n-1", "");

        var lines = canonical.Split('\n');

        Assert.Equal(5, lines.Length);
        Assert.Equal("POST", lines[0]);
        Assert.Equal("/v1/payment/create", lines[1]);
        Assert.Equal(Now.ToUnixTimeSeconds().ToString(), lines[2]);
        Assert.Equal("n-1", lines[3]);
        // SHA-256 of the empty string, hex, upper case.
        Assert.Equal(
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            lines[4]);
    }
}
