using System.Net;
using YoPay.Application.Security;

namespace YoPay.UnitTests;

public class OutboundAddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]              // loopback
    [InlineData("10.1.2.3")]               // private
    [InlineData("172.17.0.2")]             // the Docker range
    [InlineData("192.168.1.10")]           // home network
    [InlineData("169.254.169.254")]        // cloud metadata - the one that matters most
    [InlineData("100.64.0.1")]             // CGNAT
    [InlineData("0.0.0.0")]
    [InlineData("::1")]                    // IPv6 loopback
    [InlineData("fc00::1")]                // unique local
    [InlineData("fe80::1")]                // link local
    [InlineData("2001:db8::1")]            // documentation
    [InlineData("::ffff:169.254.169.254")] // IPv4-mapped: same host, different notation
    [InlineData("64:ff9b::a9fe:a9fe")]     // NAT64 back into IPv4 space
    [InlineData("2002:a9fe:a9fe::1")]      // 6to4 with an embedded IPv4 address
    public void Addresses_inside_the_trust_boundary_are_refused(string address)
    {
        Assert.False(OutboundAddressPolicy.IsPubliclyRoutable(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("203.112.0.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Ordinary_internet_addresses_are_allowed(string address)
    {
        Assert.True(OutboundAddressPolicy.IsPubliclyRoutable(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("http://example.com/hook")]        // plaintext
    [InlineData("https://example.com:8080/hook")]  // odd port
    [InlineData("ftp://example.com/hook")]
    [InlineData("https://user:pw@example.com/h")]  // credentials in the URL
    [InlineData("https://127.0.0.1/hook")]         // literal, never goes near DNS
    [InlineData("https://[::1]/hook")]
    public void Bad_webhook_urls_are_rejected_when_the_merchant_saves_them(string url)
    {
        Assert.False(OutboundAddressPolicy.TryValidateShape(new Uri(url), out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("https://shop.example.com/yopay/webhook")]
    [InlineData("https://example.com:8443/hook")]
    public void A_normal_https_endpoint_passes_the_shape_check(string url)
    {
        Assert.True(OutboundAddressPolicy.TryValidateShape(new Uri(url), out var error));
        Assert.Null(error);
    }
}
