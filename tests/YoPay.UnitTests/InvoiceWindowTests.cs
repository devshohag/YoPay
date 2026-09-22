using YoPay.Application.Invoicing;
using YoPay.Domain.Entities;

namespace YoPay.UnitTests;

public class InvoiceWindowTests
{
    [Fact]
    public void Grace_extends_past_the_customer_facing_expiry()
    {
        var openedAt = DateTimeOffset.UtcNow;

        var (expiresAt, graceUntil) = InvoiceWindow.Default.Apply(openedAt);

        Assert.True(graceUntil > expiresAt);
    }

    [Fact]
    public void A_backlog_uploaded_hours_late_still_matches_if_the_payment_was_on_time()
    {
        // The phone was offline and uploaded at 3am. What matters is when the customer
        // paid, not when we heard about it.
        var openedAt = DateTimeOffset.UtcNow.AddHours(-6);
        var (_, graceUntil) = InvoiceWindow.Default.Apply(openedAt);

        var invoice = new Invoice
        {
            MerchantId = Guid.CreateVersion7(),
            WalletId = Guid.CreateVersion7(),
            OrderRef = "ORD-1",
            Amount = 500m,
            ChargedAmount = 500m,
            CreatedAt = openedAt,
            GraceUntil = graceUntil,
        };

        var paidOnTime = openedAt.AddMinutes(4);

        Assert.True(InvoiceWindow.Default.Accepts(invoice, paidOnTime));
    }

    [Fact]
    public void A_payment_made_after_the_grace_period_is_not_accepted()
    {
        var openedAt = DateTimeOffset.UtcNow.AddHours(-6);
        var (_, graceUntil) = InvoiceWindow.Default.Apply(openedAt);

        var invoice = new Invoice
        {
            MerchantId = Guid.CreateVersion7(),
            WalletId = Guid.CreateVersion7(),
            OrderRef = "ORD-2",
            Amount = 500m,
            ChargedAmount = 500m,
            CreatedAt = openedAt,
            GraceUntil = graceUntil,
        };

        Assert.False(InvoiceWindow.Default.Accepts(invoice, graceUntil.AddMinutes(1)));
    }
}
