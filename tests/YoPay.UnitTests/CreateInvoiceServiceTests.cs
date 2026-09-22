using YoPay.Application.Invoicing;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;

namespace YoPay.UnitTests;

public class CreateInvoiceServiceTests
{
    private static readonly Guid MerchantId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private static (CreateInvoiceService Service, FakeInvoiceStore Store) Build()
    {
        var store = new FakeInvoiceStore();

        store.Wallets.Add(new Wallet
        {
            MerchantId = MerchantId,
            Method = PaymentMethod.Bkash,
            Number = "01700000000",
            AccountType = WalletAccountType.PersonalRetail,
        });

        return (new CreateInvoiceService(store, new FixedClock(Now)), store);
    }

    private static CreateInvoiceCommand Command(string orderRef = "ORD-1", decimal amount = 500m) => new()
    {
        MerchantId = MerchantId,
        OrderRef = orderRef,
        Amount = amount,
        Method = PaymentMethod.Bkash,
    };

    [Fact]
    public async Task A_new_order_creates_an_invoice_and_its_session_together()
    {
        var (service, store) = Build();

        var result = await service.CreateAsync(Command());

        Assert.Equal(CreateInvoiceOutcome.Created, result.Outcome);
        Assert.Single(store.Invoices);
        Assert.Single(store.Sessions);
        Assert.Equal(store.Invoices[0].Id, store.Sessions[0].InvoiceId);
        Assert.Equal(InvoiceStatus.AwaitingPayment, store.Invoices[0].Status);
    }

    [Fact]
    public async Task Retrying_the_same_order_returns_the_first_invoice_and_writes_nothing()
    {
        // Merchants retry on timeouts. A second invoice for one order means a customer
        // paying twice, so the retry has to be a read.
        var (service, store) = Build();

        var first = await service.CreateAsync(Command());
        var second = await service.CreateAsync(Command());

        Assert.Equal(CreateInvoiceOutcome.AlreadyExists, second.Outcome);
        Assert.Equal(first.Invoice!.Id, second.Invoice!.Id);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task The_session_window_closes_before_the_grace_period_does()
    {
        var (service, store) = Build();

        await service.CreateAsync(Command());

        var invoice = store.Invoices[0];
        Assert.Equal(invoice.ExpiresAt, store.Sessions[0].ExpiresAt);
        Assert.True(invoice.GraceUntil > invoice.ExpiresAt);
    }

    [Fact]
    public async Task Trxid_mode_charges_exactly_what_was_billed()
    {
        var (service, store) = Build();

        await service.CreateAsync(Command(amount: 500m));

        Assert.Equal(500m, store.Invoices[0].Amount);
        Assert.Equal(500m, store.Invoices[0].ChargedAmount);
    }

    [Fact]
    public async Task Unique_amount_mode_salts_around_an_amount_already_in_flight()
    {
        var (service, store) = Build();
        store.OpenAmounts.Add(500m);

        var result = await service.CreateAsync(Command(), MatchingMode.UniqueAmount);

        Assert.Equal(MatchingMode.UniqueAmount, result.Mode);
        Assert.Equal(500m, result.Invoice!.Amount);
        Assert.Equal(501m, result.Invoice.ChargedAmount);
    }

    [Fact]
    public async Task When_the_slots_run_out_the_invoice_falls_back_instead_of_failing()
    {
        // Fifty taka has two slots. Both taken, so this invoice switches to the flow that
        // does not need the amount to be unique - the customer is not turned away.
        var (service, store) = Build();
        store.OpenAmounts.AddRange([50m, 51m]);

        var result = await service.CreateAsync(Command(amount: 50m), MatchingMode.UniqueAmount);

        Assert.Equal(CreateInvoiceOutcome.Created, result.Outcome);
        Assert.Equal(MatchingMode.TrxId, result.Mode);
        Assert.Equal(50m, result.Invoice!.ChargedAmount);
    }

    [Fact]
    public async Task A_merchant_with_no_wallet_for_that_method_is_told_so()
    {
        var (service, _) = Build();

        var result = await service.CreateAsync(Command() with { Method = PaymentMethod.Nagad });

        Assert.Equal(CreateInvoiceOutcome.NoWallet, result.Outcome);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task A_non_positive_amount_is_rejected(decimal amount)
    {
        var (service, store) = Build();

        var result = await service.CreateAsync(Command(amount: amount));

        Assert.Equal(CreateInvoiceOutcome.InvalidAmount, result.Outcome);
        Assert.Empty(store.Invoices);
    }
}
