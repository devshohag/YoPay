using YoPay.Domain.Invoicing;

namespace YoPay.Domain.Tests;

public class AmountToleranceTests
{
    [Fact]
    public void Exact_tolerance_treats_a_shortfall_as_partial()
    {
        var tolerance = AmountTolerance.Exact;

        Assert.False(tolerance.IsFullPayment(expected: 500m, received: 499m));
        Assert.True(tolerance.IsFullPayment(expected: 500m, received: 500m));
    }

    [Fact]
    public void A_merchant_may_accept_a_small_shortfall()
    {
        var tolerance = new AmountTolerance(AbsoluteBdt: 5m);

        Assert.True(tolerance.IsFullPayment(expected: 500m, received: 495m));
        Assert.False(tolerance.IsFullPayment(expected: 500m, received: 494m));
    }

    [Fact]
    public void Overpayment_is_flagged_rather_than_swallowed()
    {
        var tolerance = new AmountTolerance(AbsoluteBdt: 5m);

        Assert.True(tolerance.IsOverpayment(expected: 500m, received: 520m));
        Assert.False(tolerance.IsOverpayment(expected: 500m, received: 503m));
    }
}
