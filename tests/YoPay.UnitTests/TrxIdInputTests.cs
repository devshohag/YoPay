using YoPay.Application.Invoicing;

namespace YoPay.UnitTests;

public class TrxIdInputTests
{
    [Theory]
    [InlineData("DHD6EYO3HO")]
    [InlineData("dhd6eyo3ho")]
    [InlineData("  DHD6EYO3HO  ")]
    [InlineData("DHD6 EYO3 HO")]
    [InlineData("DHD6-EYO3-HO")]
    public void However_it_is_typed_it_normalises_to_the_same_id(string typed)
    {
        // Lower case, stray spaces, dashes from a phone keyboard. Each of these would
        // otherwise be a payment sitting unmatched while the customer insists they paid.
        Assert.Equal("DHD6EYO3HO", TrxIdInput.Normalise(typed));
    }

    [Fact]
    public void A_whole_pasted_sms_yields_the_id_inside_it()
    {
        // What people actually do: long-press, copy all, paste.
        const string sms =
            "You have received Tk 200.00 from 01910126335. Fee Tk 0.00. " +
            "Balance Tk 1,881.86. TrxID DHD6EYO3HO at 13/08/2026 16:02";

        Assert.Equal("DHD6EYO3HO", TrxIdInput.Normalise(sms));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("hello")]
    [InlineData("12345")]
    [InlineData("I paid already, please check")]
    public void Nothing_usable_yields_nothing_rather_than_a_guess(string? typed)
    {
        Assert.Null(TrxIdInput.Normalise(typed));
        Assert.False(TrxIdInput.IsWellFormed(typed));
    }

    [Fact]
    public void An_id_of_the_wrong_length_is_not_accepted()
    {
        Assert.Null(TrxIdInput.Normalise("DHD6EYO3H"));
        Assert.Null(TrxIdInput.Normalise("DHD6EYO3HOX"));
    }
}
