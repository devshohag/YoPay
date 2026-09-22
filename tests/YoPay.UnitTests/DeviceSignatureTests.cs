using System.Security.Cryptography;
using YoPay.Application.Devices;
using YoPay.Application.Security;

namespace YoPay.UnitTests;

public class DeviceSignatureTests
{
    private const string Canonical = "POST\n/v1/ingest/events\n1758600000\nn-1\nABCD";

    private static (string PublicKey, ECDsa Key) NewDevice()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), key);
    }

    [Fact]
    public void A_handset_signature_verifies_against_its_public_key()
    {
        var (publicKey, key) = NewDevice();
        using var _ = key;

        var signature = DeviceSignature.Sign(key, Canonical);

        Assert.True(DeviceSignature.Verify(publicKey, Canonical, signature));
    }

    [Fact]
    public void Another_handset_cannot_sign_for_this_one()
    {
        // The point of asymmetric keys here: a stolen database yields no device's key.
        var (publicKey, key) = NewDevice();
        var (_, other) = NewDevice();
        using var _ = key;
        using var __ = other;

        Assert.False(DeviceSignature.Verify(publicKey, Canonical, DeviceSignature.Sign(other, Canonical)));
    }

    [Fact]
    public void Changing_the_request_breaks_the_signature()
    {
        var (publicKey, key) = NewDevice();
        using var _ = key;

        var signature = DeviceSignature.Sign(key, Canonical);

        Assert.False(DeviceSignature.Verify(
            publicKey, Canonical.Replace("/events", "/heartbeat", StringComparison.Ordinal), signature));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("YWJjZA==")]
    public void A_malformed_key_or_signature_is_a_failed_check_not_a_crash(string junk)
    {
        Assert.False(DeviceSignature.Verify(junk, Canonical, junk));
    }

    [Fact]
    public void The_canonical_string_is_the_same_shape_the_merchant_api_uses()
    {
        // One format to document, one to get right.
        var canonical = RequestSignature.Canonicalise(
            "POST", "/v1/ingest/events", DateTimeOffset.UnixEpoch, "n", "");

        Assert.Equal(5, canonical.Split('\n').Length);
    }
}
