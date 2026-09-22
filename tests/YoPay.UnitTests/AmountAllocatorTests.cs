using YoPay.Application.Invoicing;

namespace YoPay.UnitTests;

public class AmountAllocatorTests
{
    [Fact]
    public void An_empty_wallet_charges_the_exact_amount()
    {
        Assert.Equal(500m, AmountAllocator.Allocate(500m, []));
    }

    [Fact]
    public void A_taken_amount_is_salted_upward()
    {
        // Upward, never downward: a downward salt costs the merchant money on every
        // single order.
        Assert.Equal(501m, AmountAllocator.Allocate(500m, [500m]));
        Assert.Equal(502m, AmountAllocator.Allocate(500m, [500m, 501m]));
    }

    [Fact]
    public void The_salt_ceiling_scales_with_the_invoice()
    {
        // Five taka on top of five thousand is noise. Five on top of fifty is a ten
        // percent surcharge, so a small invoice gets a small ceiling.
        Assert.Equal(1m, AmountAllocator.MaxSaltFor(50m));
        Assert.Equal(5m, AmountAllocator.MaxSaltFor(5000m));
        Assert.Equal(3m, AmountAllocator.MaxSaltFor(150m));
    }

    [Fact]
    public void A_small_invoice_runs_out_of_slots_quickly_and_says_so()
    {
        // Two slots at fifty taka. This is a real capacity limit on the unique-amount
        // strategy, not an oversight, and the caller answers it by falling back to the
        // transaction id flow rather than distorting the price further.
        Assert.Null(AmountAllocator.Allocate(50m, [50m, 51m]));
    }

    [Fact]
    public void A_larger_invoice_has_room()
    {
        Assert.Equal(1005m, AmountAllocator.Allocate(1000m, [1000m, 1001m, 1002m, 1003m, 1004m]));
        Assert.Null(AmountAllocator.Allocate(1000m, [1000m, 1001m, 1002m, 1003m, 1004m, 1005m]));
    }

    [Fact]
    public void Amounts_taken_by_other_invoices_do_not_interfere()
    {
        Assert.Equal(500m, AmountAllocator.Allocate(500m, [300m, 750m, 1200m]));
    }

    [Fact]
    public void A_non_positive_amount_is_a_programming_error_not_a_null()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountAllocator.Allocate(0m, []));
    }
}
