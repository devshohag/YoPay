using YoPay.Application.Devices;

namespace YoPay.UnitTests;

public class PairingTokenTests
{
    [Fact]
    public void A_token_is_three_readable_blocks()
    {
        var token = PairingToken.New();

        var blocks = token.Split('-');

        Assert.Equal(3, blocks.Length);
        Assert.All(blocks, b => Assert.Equal(4, b.Length));
    }

    [Fact]
    public void The_alphabet_leaves_out_the_characters_people_mishear()
    {
        // A merchant may have to read this down a phone line to whoever holds the handset.
        var sample = string.Concat(Enumerable.Range(0, 200).Select(_ => PairingToken.New()));

        Assert.DoesNotContain('I', sample);
        Assert.DoesNotContain('O', sample);
        Assert.DoesNotContain('0', sample);
        Assert.DoesNotContain('S', sample);
    }

    [Theory]
    [InlineData("abcd-efgh-jklm")]
    [InlineData("ABCDEFGHJKLM")]
    [InlineData("  abcd efgh jklm  ")]
    public void It_can_be_typed_however_the_person_manages(string typed)
    {
        Assert.Equal(PairingToken.Hash("ABCD-EFGH-JKLM"), PairingToken.Hash(typed));
    }

    [Fact]
    public void A_different_token_hashes_differently()
    {
        Assert.NotEqual(PairingToken.Hash("ABCD-EFGH-JKLM"), PairingToken.Hash("ABCD-EFGH-JKLN"));
    }

    [Fact]
    public void The_token_itself_is_never_what_gets_stored()
    {
        var token = PairingToken.New();

        Assert.DoesNotContain(PairingToken.Normalise(token), PairingToken.Hash(token), StringComparison.Ordinal);
    }
}
