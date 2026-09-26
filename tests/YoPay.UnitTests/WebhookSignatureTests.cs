using YoPay.Application.Webhooks;

namespace YoPay.UnitTests;

/// <summary>
/// The signature is the only thing standing between a merchant and anyone who finds their
/// webhook URL. A shop that trusts an unsigned body ships goods for a POST that cost the
/// sender nothing, so these tests are about what must be refused rather than what works.
/// </summary>
public class WebhookSignatureTests
{
    private const string Secret = "sxJx3Xp1G0s1kM8oZ4qYv2mB6nC9dF7hK0lP3rT5uW8=";
    private const string Body = """{"event":"payment.paid","amount":500.00}""";

    private static readonly DateTimeOffset Now = new(2026, 9, 24, 17, 5, 0, TimeSpan.Zero);

    [Fact]
    public void A_signature_we_made_verifies()
    {
        var header = WebhookSignature.Sign(Secret, Now, Body);

        Assert.True(WebhookSignature.Verify(Secret, header, Body, Now));
    }

    [Fact]
    public void The_header_carries_a_timestamp_and_a_version()
    {
        var header = WebhookSignature.Sign(Secret, Now, Body);

        // The version tag is how the algorithm changes one day without breaking every
        // integration on the same morning.
        Assert.StartsWith("t=1790269500,v1=", header, StringComparison.Ordinal);
    }

    [Fact]
    public void A_different_secret_does_not_verify()
    {
        var header = WebhookSignature.Sign(Secret, Now, Body);

        Assert.False(WebhookSignature.Verify("someone-elses-secret", header, Body, Now));
    }

    [Fact]
    public void An_edited_body_does_not_verify()
    {
        // The whole point: change the amount in flight and the MAC stops matching.
        var header = WebhookSignature.Sign(Secret, Now, Body);

        Assert.False(WebhookSignature.Verify(
            Secret, header, Body.Replace("500.00", "5.00", StringComparison.Ordinal), Now));
    }

    [Fact]
    public void A_captured_delivery_replayed_next_week_does_not_verify()
    {
        // Why the timestamp is inside the MAC rather than beside it. Signing the body
        // alone would produce a token that is valid forever, and a "paid" notification
        // that never expires is a free order for whoever recorded one.
        var header = WebhookSignature.Sign(Secret, Now, Body);

        Assert.False(WebhookSignature.Verify(Secret, header, Body, Now.AddDays(7)));
    }

    [Fact]
    public void A_timestamp_moved_to_look_fresh_does_not_verify()
    {
        // And why it cannot simply be rewritten: the timestamp is covered by the MAC, so
        // changing it to something recent invalidates the signature it travels with.
        var header = WebhookSignature.Sign(Secret, Now, Body);
        var forged = header.Replace(
            $"t={Now.ToUnixTimeSeconds()}",
            $"t={Now.AddDays(7).ToUnixTimeSeconds()}",
            StringComparison.Ordinal);

        Assert.False(WebhookSignature.Verify(Secret, forged, Body, Now.AddDays(7)));
    }

    [Fact]
    public void Modest_clock_drift_between_two_servers_is_tolerated()
    {
        var header = WebhookSignature.Sign(Secret, Now, Body);

        Assert.True(WebhookSignature.Verify(Secret, header, Body, Now.AddMinutes(4)));
        Assert.True(WebhookSignature.Verify(Secret, header, Body, Now.AddMinutes(-4)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("v1=abc")]
    [InlineData("t=1790269500")]
    [InlineData("t=notanumber,v1=abc")]
    public void A_malformed_header_is_a_failed_verification_not_a_crash(string header)
    {
        Assert.False(WebhookSignature.Verify(Secret, header, Body, Now));
    }

    [Fact]
    public void An_empty_secret_never_verifies()
    {
        // A merchant who has not configured a secret must fail closed, not open.
        var header = WebhookSignature.Sign(Secret, Now, Body);

        Assert.False(WebhookSignature.Verify("", header, Body, Now));
    }
}
