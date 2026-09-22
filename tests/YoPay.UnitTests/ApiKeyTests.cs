using YoPay.Application.Security;

namespace YoPay.UnitTests;

public class ApiKeyTests
{
    [Fact]
    public void An_issued_key_verifies_against_its_stored_hash()
    {
        var (keyId, secret, hash) = ApiKey.Issue();

        Assert.NotEmpty(keyId);
        Assert.StartsWith(ApiKey.Prefix, secret, StringComparison.Ordinal);
        Assert.True(ApiKey.Matches(secret, hash));
    }

    [Fact]
    public void Another_key_does_not()
    {
        var (_, _, hash) = ApiKey.Issue();
        var (_, other, _) = ApiKey.Issue();

        Assert.False(ApiKey.Matches(other, hash));
    }

    [Fact]
    public void Two_issues_never_collide()
    {
        var (idA, secretA, _) = ApiKey.Issue();
        var (idB, secretB, _) = ApiKey.Issue();

        Assert.NotEqual(idA, idB);
        Assert.NotEqual(secretA, secretB);
    }

    [Fact]
    public void Empty_input_is_never_a_match()
    {
        var (_, _, hash) = ApiKey.Issue();

        Assert.False(ApiKey.Matches("", hash));
        Assert.False(ApiKey.Matches("x", ""));
    }
}
