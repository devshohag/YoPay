using YoPay.Application.Abstractions;
using YoPay.Application.Invoicing;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;

namespace YoPay.UnitTests;

/// <summary>
/// Which of the two checkout flows an invoice ends up on.
///
/// TrxId asks the customer to copy a transaction id. UniqueAmount asks for nothing and
/// recognises the payment by an exact figure reserved on the wallet. The second is the
/// experience worth having; the first is the one that cannot mismatch.
///
/// The decision is made once, at creation, and stored on the invoice - because the
/// checkout page and the matcher read it at different moments, and an invoice that
/// changed flows underneath an open page would be a customer told to do one thing and
/// judged by another.
/// </summary>
public class MatchingModeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Merchant = Guid.CreateVersion7();

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private static FakeInvoiceStore StoreWith(params decimal[] takenAmounts)
    {
        var store = new FakeInvoiceStore();

        store.Wallets.Add(new Wallet
        {
            MerchantId = Merchant,
            Method = PaymentMethod.Bkash,
            Number = "01700000000",
            AccountType = WalletAccountType.PersonalRetail,
            IsActive = true,
        });

        store.OpenAmounts.AddRange(takenAmounts);

        return store;
    }

    private static CreateInvoiceCommand Command(decimal amount = 500m) => new()
    {
        MerchantId = Merchant,
        OrderRef = "ORD-1041",
        Amount = amount,
        Method = PaymentMethod.Bkash,
    };

    private static CreateInvoiceService Service(FakeInvoiceStore store) =>
        new(store, new FixedClock());

    [Fact]
    public async Task An_invoice_is_on_the_transaction_id_flow_unless_asked_otherwise()
    {
        var result = await Service(StoreWith()).CreateAsync(Command());

        Assert.Equal(MatchingMode.TrxId, result.Invoice!.Mode);
        Assert.Equal(500m, result.Invoice.ChargedAmount);
    }

    [Fact]
    public async Task The_invoice_itself_remembers_the_flow_not_just_the_result()
    {
        // The checkout page is handed an invoice, not a CreateInvoiceResult. If the mode
        // lived only on the result, the page would have nothing to read and would have to
        // guess - which is how a customer ends up shown a box nobody will look at.
        var result = await Service(StoreWith()).CreateAsync(Command(), MatchingMode.UniqueAmount);

        Assert.Equal(MatchingMode.UniqueAmount, result.Invoice!.Mode);
        Assert.Equal(result.Mode, result.Invoice.Mode);
    }

    [Fact]
    public async Task The_zero_input_flow_reserves_a_figure_nobody_else_is_expecting()
    {
        var result = await Service(StoreWith(500m, 501m))
            .CreateAsync(Command(), MatchingMode.UniqueAmount);

        Assert.Equal(502m, result.Invoice!.ChargedAmount);
        Assert.Equal(MatchingMode.UniqueAmount, result.Invoice.Mode);
    }

    [Fact]
    public async Task The_customer_is_never_asked_for_less_than_the_price()
    {
        // The salt goes up, never down. Either direction makes the figure unambiguous,
        // but a downward one quietly costs the merchant money on every order.
        var result = await Service(StoreWith(500m))
            .CreateAsync(Command(), MatchingMode.UniqueAmount);

        Assert.True(result.Invoice!.ChargedAmount >= result.Invoice.Amount);
    }

    [Fact]
    public async Task When_the_wallet_has_no_free_figure_left_the_invoice_falls_back()
    {
        // The salt is whole taka, at most two percent of the price and never above five,
        // so a 500 taka invoice has six slots. Filling them is an ordinary afternoon on a
        // busy wallet, not an error - and widening the salt until the number stops
        // resembling the price would be worse than asking for a transaction id.
        var everySlot = Enumerable.Range(0, 6).Select(i => 500m + i).ToArray();

        var result = await Service(StoreWith(everySlot))
            .CreateAsync(Command(), MatchingMode.UniqueAmount);

        Assert.Equal(MatchingMode.TrxId, result.Invoice!.Mode);
        Assert.Equal(500m, result.Invoice.ChargedAmount);
    }

    [Fact]
    public async Task A_fallback_is_visible_rather_than_silent()
    {
        // A caller who asked for zero input and got the other flow has to be able to see
        // that, or they will show the customer a page that contradicts itself.
        var everySlot = Enumerable.Range(0, 6).Select(i => 500m + i).ToArray();

        var result = await Service(StoreWith(everySlot))
            .CreateAsync(Command(), MatchingMode.UniqueAmount);

        Assert.NotEqual(MatchingMode.UniqueAmount, result.Mode);
        Assert.Equal(result.Invoice!.Mode, result.Mode);
    }
}
