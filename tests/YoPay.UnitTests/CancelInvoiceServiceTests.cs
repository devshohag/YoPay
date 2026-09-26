using YoPay.Application.Abstractions;
using YoPay.Application.Invoicing;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;

namespace YoPay.UnitTests;

public class CancelInvoiceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Merchant = Guid.CreateVersion7();

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private static (CancelInvoiceService Service, FakeInvoiceStore Store, Invoice Invoice) Setup(
        InvoiceStatus status = InvoiceStatus.AwaitingPayment)
    {
        var invoice = new Invoice
        {
            MerchantId = Merchant,
            WalletId = Guid.CreateVersion7(),
            OrderRef = "ORD-1041",
            Amount = 500m,
            ChargedAmount = 500m,
            Currency = "BDT",
            Status = status,
            ExpiresAt = Now.AddMinutes(25),
            GraceUntil = Now.AddMinutes(40),
        };

        var store = new FakeInvoiceStore();
        store.Invoices.Add(invoice);

        store.Sessions.Add(new PaymentSession
        {
            InvoiceId = invoice.Id,
            WalletId = invoice.WalletId,
            ExpectedAmount = 500m,
            State = SessionState.Open,
            OpenedAt = Now,
            ExpiresAt = Now.AddMinutes(25),
        });

        return (new CancelInvoiceService(store, new FixedClock()), store, invoice);
    }

    [Fact]
    public async Task An_unpaid_invoice_is_cancelled()
    {
        var (service, _, invoice) = Setup();

        var result = await service.CancelAsync(Merchant, invoice.Id, null);

        Assert.Equal(CancelInvoiceOutcome.Cancelled, result.Outcome);
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    [Fact]
    public async Task Cancelling_frees_the_amount_the_session_was_holding()
    {
        // The reason this operation exists. While the session is open no other invoice on
        // that wallet may expect the same amount, so a shop that abandons carts and never
        // cancels slowly runs out of usable amounts.
        var (service, store, invoice) = Setup();

        await service.CancelAsync(Merchant, invoice.Id, null);

        Assert.Equal(SessionState.Cancelled, store.Sessions[0].State);
    }

    [Fact]
    public async Task It_can_be_found_by_the_merchants_own_order_reference()
    {
        // Which is what a shop actually has in its database.
        var (service, _, _) = Setup();

        var result = await service.CancelAsync(Merchant, null, "ORD-1041");

        Assert.Equal(CancelInvoiceOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task Cancelling_twice_is_success_not_an_error()
    {
        // The caller is a retrying HTTP client. The state it asked for is the state it has.
        var (service, _, invoice) = Setup();

        await service.CancelAsync(Merchant, invoice.Id, null);
        var second = await service.CancelAsync(Merchant, invoice.Id, null);

        Assert.Equal(CancelInvoiceOutcome.AlreadyCancelled, second.Outcome);
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task A_paid_invoice_is_refused_and_says_why()
    {
        // Money has arrived. Walking the row backwards would not return it, and an order
        // marked cancelled that was actually paid is how a shop loses goods.
        var (service, _, invoice) = Setup(InvoiceStatus.Paid);

        var result = await service.CancelAsync(Merchant, invoice.Id, null);

        Assert.Equal(CancelInvoiceOutcome.Refused, result.Outcome);
        Assert.Contains("refund", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public async Task Another_merchants_invoice_is_simply_not_found()
    {
        // Not "forbidden": the lookup is scoped by merchant, so the answer cannot be used
        // to discover which invoice ids exist.
        var (service, _, invoice) = Setup();

        var result = await service.CancelAsync(Guid.CreateVersion7(), invoice.Id, null);

        Assert.Equal(CancelInvoiceOutcome.NotFound, result.Outcome);
        Assert.Equal(InvoiceStatus.AwaitingPayment, invoice.Status);
    }

    [Fact]
    public async Task An_unknown_order_reference_is_not_found()
    {
        var (service, _, _) = Setup();

        var result = await service.CancelAsync(Merchant, null, "ORD-nope");

        Assert.Equal(CancelInvoiceOutcome.NotFound, result.Outcome);
    }
}
