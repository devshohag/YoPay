using System.Security.Cryptography;
using YoPay.Application.Security;

namespace YoPay.UnitTests;

public class SecretProtectorTests
{
    private const string KeyA = "3q2+796tvu/erb7v3q2+796tvu/erb7v3q2+796tvu8=";
    private const string KeyB = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private static SecretProtectionOptions Ring(string active) => new()
    {
        ActiveKeyId = active,
        Keys = new Dictionary<string, string> { ["k1"] = KeyA, ["k2"] = KeyB },
    };

    [Fact]
    public void A_secret_survives_a_round_trip()
    {
        var protector = new AesGcmSecretProtector(Ring("k1"));

        var ciphertext = protector.Protect("hmac-secret-value");

        Assert.Equal("hmac-secret-value", protector.Unprotect(ciphertext));
    }

    [Fact]
    public void The_same_plaintext_encrypts_differently_every_time()
    {
        // A fresh nonce per call. Identical ciphertext would tell an observer with read
        // access to the table which merchants share a secret.
        var protector = new AesGcmSecretProtector(Ring("k1"));

        Assert.NotEqual(protector.Protect("same"), protector.Protect("same"));
    }

    [Fact]
    public void The_ciphertext_names_the_key_that_wrote_it()
    {
        var protector = new AesGcmSecretProtector(Ring("k2"));

        Assert.True(SecretEnvelope.TryParse(protector.Protect("x"), out var envelope));
        Assert.Equal("k2", envelope.KeyId);
        Assert.Equal(SecretEnvelope.CurrentVersion, envelope.Version);
    }

    [Fact]
    public void A_value_written_with_a_retired_key_is_still_readable_after_rotation()
    {
        // This is the entire reason the key ring exists. Rotating the active key must not
        // lock the service out of everything written before the rotation.
        var before = new AesGcmSecretProtector(Ring("k1"));
        var ciphertext = before.Protect("issued-before-rotation");

        var after = new AesGcmSecretProtector(Ring("k2"));

        Assert.Equal("issued-before-rotation", after.Unprotect(ciphertext));
        Assert.True(after.NeedsRotation(ciphertext));
        Assert.False(after.NeedsRotation(after.Protect("issued-after")));
    }

    [Fact]
    public void Editing_the_key_id_in_a_stored_value_fails_the_tag_check()
    {
        // The key id is authenticated as associated data, not merely written in the
        // header, so someone with write access to the row cannot repoint a ciphertext.
        var protector = new AesGcmSecretProtector(Ring("k1"));
        var tampered = protector.Protect("x").Replace(".k1.", ".k2.", StringComparison.Ordinal);

        Assert.Throws<AuthenticationTagMismatchException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void A_key_that_is_no_longer_on_the_ring_fails_as_an_operations_problem()
    {
        var protector = new AesGcmSecretProtector(Ring("k1"));
        var ciphertext = protector.Protect("x");

        var shrunk = new AesGcmSecretProtector(new SecretProtectionOptions
        {
            ActiveKeyId = "k2",
            Keys = new Dictionary<string, string> { ["k2"] = KeyB },
        });

        var ex = Assert.Throws<CryptographicException>(() => shrunk.Unprotect(ciphertext));
        Assert.Contains("retired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_key_ring_whose_active_key_is_missing_fails_at_startup_not_at_first_use()
    {
        var options = new SecretProtectionOptions
        {
            ActiveKeyId = "k9",
            Keys = new Dictionary<string, string> { ["k1"] = KeyA },
        };

        Assert.Throws<InvalidOperationException>(() => new AesGcmSecretProtector(options));
    }

    [Fact]
    public void A_key_of_the_wrong_length_is_rejected()
    {
        var options = new SecretProtectionOptions
        {
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = Convert.ToBase64String(new byte[16]) },
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
