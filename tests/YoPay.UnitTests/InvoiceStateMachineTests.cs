using YoPay.Domain.Common;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Domain.Invoicing;

namespace YoPay.UnitTests;

public class InvoiceStateMachineTests
{
    private static Invoice NewInvoice(InvoiceStatus status) => new()
    {
        MerchantId = Guid.CreateVersion7(),
        WalletId = Guid.CreateVersion7(),
        OrderRef = "ORD-1",
        Amount = 500m,
        ChargedAmount = 500m,
        Status = status,
    };

    [Fact]
    public void AwaitingPayment_can_become_paid()
    {
        var invoice = NewInvoice(InvoiceStatus.AwaitingPayment);
        var now = DateTimeOffset.UtcNow;

        InvoiceStateMachine.Transition(invoice, InvoiceStatus.Paid, now);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(now, invoice.PaidAt);
    }

    [Fact]
    public void Paid_never_walks_backwards()
    {
        // A duplicate message arriving late must not reopen an order that already shipped.
        var invoice = NewInvoice(InvoiceStatus.Paid);

        Assert.Throws<DomainException>(() =>
            InvoiceStateMachine.Transition(invoice, InvoiceStatus.AwaitingPayment, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Repeating_the_same_transition_is_a_no_op()
    {
        // Events are delivered at least once, so replaying one must not throw.
        var invoice = NewInvoice(InvoiceStatus.Paid);
        var paidAt = invoice.PaidAt;

        InvoiceStateMachine.Transition(invoice, InvoiceStatus.Paid, DateTimeOffset.UtcNow);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(paidAt, invoice.PaidAt);
    }

    [Fact]
    public void An_elapsed_window_goes_to_review_not_straight_to_expired()
    {
        // Operator messages run late and offline devices upload late; failing an invoice
        // the moment its window closes would reject payments that were made on time.
        Assert.False(InvoiceStateMachine.CanTransition(
            InvoiceStatus.AwaitingPayment, InvoiceStatus.Expired));

        Assert.True(InvoiceStateMachine.CanTransition(
            InvoiceStatus.AwaitingPayment, InvoiceStatus.PendingReview));

        Assert.True(InvoiceStateMachine.CanTransition(
            InvoiceStatus.PendingReview, InvoiceStatus.Expired));
    }

    [Fact]
    public void A_late_payment_still_rescues_an_invoice_under_review()
    {
        var invoice = NewInvoice(InvoiceStatus.PendingReview);

        InvoiceStateMachine.Transition(invoice, InvoiceStatus.Paid, DateTimeOffset.UtcNow);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Expired)]
    [InlineData(InvoiceStatus.Cancelled)]
    [InlineData(InvoiceStatus.Settled)]
    public void Terminal_states_have_no_exits(InvoiceStatus status)
    {
        Assert.True(InvoiceStateMachine.IsTerminal(status));
        Assert.Empty(InvoiceStateMachine.NextStates(status));
    }
}
