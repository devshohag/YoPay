using YoPay.Application.Invoicing;

namespace YoPay.UnitTests;

public class ReferenceCodeTests
{
    [Fact]
    public void A_code_is_the_prefix_plus_six_characters()
    {
        var code = ReferenceCode.New();

        Assert.StartsWith(ReferenceCode.Prefix, code, StringComparison.Ordinal);
        Assert.Equal(ReferenceCode.Prefix.Length + ReferenceCode.Length, code.Length);
    }

    [Fact]
    public void The_alphabet_leaves_out_the_characters_people_misread()
    {
        // Typed off one screen into another, usually on a phone. I/1, O/0 and S/5 are
        // where that goes wrong.
        var sample = string.Concat(Enumerable.Range(0, 200).Select(_ => ReferenceCode.New()));

        Assert.DoesNotContain('I', sample);
        Assert.DoesNotContain('O', sample);
        Assert.DoesNotContain('0', sample);
        Assert.DoesNotContain('1', sample);
        Assert.DoesNotContain('S', sample);
        Assert.DoesNotContain('5', sample);
    }

    [Theory]
    [InlineData("yp ab cd ef")]
    [InlineData("YPABCDEF")]
    [InlineData("  ypabcdef  ")]
    [InlineData("ABCDEF")]
    [InlineData("yp-abcdef")]
    public void Customers_type_it_however_they_like_and_it_still_matches(string typed)
    {
        // Lower case, spaces, dashes, prefix left off. None of that should mean a payment
        // goes unmatched.
        Assert.True(ReferenceCode.Matches("YPABCDEF", typed));
    }

    [Fact]
    public void A_different_code_does_not_match()
    {
        Assert.False(ReferenceCode.Matches("YPABCDEF", "YPZZZZZZ"));
        Assert.False(ReferenceCode.Matches("YPABCDEF", null));
    }
}
