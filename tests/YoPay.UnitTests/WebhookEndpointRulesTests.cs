using YoPay.Application.Webhooks;

namespace YoPay.UnitTests;

/// <summary>
/// What a merchant is allowed to point a webhook at.
///
/// A merchant-supplied URL is a server-side request forgery primitive: they register a
/// host that resolves inward, YoPay fetches it from inside its own trust boundary, and
/// because the response code lands on a delivery row the merchant can read, it is not
/// even blind. These are the rules that stop it, and the loosened set below is the only
/// sanctioned way around them.
/// </summary>
public class WebhookEndpointRulesTests
{
    [Theory]
    [InlineData("https://shop.example.com/yopay")]
    [InlineData("https://shop.example.com:8443/yopay")]
    public void An_ordinary_public_endpoint_is_accepted(string url)
    {
        Assert.True(WebhookEndpointRules.Check(url).Accepted);
    }

    [Theory]
    [InlineData("http://shop.example.com/yopay")]
    [InlineData("http://localhost:5090/yopay")]
    [InlineData("https://127.0.0.1/yopay")]
    [InlineData("https://10.0.0.5/yopay")]
    [InlineData("https://192.168.1.10/yopay")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://user:password@shop.example.com/yopay")]
    [InlineData("https://shop.example.com:22/yopay")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://shop.example.com/1")]
    [InlineData("")]
    [InlineData("not a url at all")]
    public void Everything_that_should_not_be_reachable_is_refused(string url)
    {
        var check = WebhookEndpointRules.Check(url);

        Assert.False(check.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(check.Error));
    }

    [Theory]
    [InlineData("http://localhost:5090/yopay")]
    [InlineData("http://127.0.0.1:5000/yopay")]
    [InlineData("https://shop.example.com/yopay")]
    public void A_developer_running_both_halves_on_one_laptop_can_be_let_through(string url)
    {
        // The switch exists so nobody edits the guard by hand to get through an afternoon,
        // because that edit is what reaches production and stays there.
        Assert.True(WebhookEndpointRules.Check(url, allowPrivate: true).Accepted);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://shop.example.com/1")]
    [InlineData("http://user:password@localhost/yopay")]
    [InlineData("")]
    [InlineData("not a url at all")]
    public void But_the_switch_does_not_allow_anything_at_all(string url)
    {
        // A scheme that is not http or https, or credentials in the URL, are mistakes on
        // any machine on any day.
        Assert.False(WebhookEndpointRules.Check(url, allowPrivate: true).Accepted);
    }

    [Fact]
    public void The_switch_is_off_unless_something_turns_it_on()
    {
        Assert.False(new WebhookOptions().AllowPrivateEndpoints);
    }

    [Fact]
    public void Each_secret_is_new_and_long_enough_to_key_the_mac()
    {
        // 32 bytes because the MAC is SHA-256, and a key shorter than the digest is the
        // weakest part of the construction.
        Assert.Equal(32, Convert.FromBase64String(WebhookEndpointRules.NewSecret()).Length);
        Assert.NotEqual(WebhookEndpointRules.NewSecret(), WebhookEndpointRules.NewSecret());
    }
}
