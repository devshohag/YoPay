using YoPay.Application.Invoicing;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;

namespace YoPay.UnitTests;

public class SubmitClaimServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid InvoiceId = Guid.CreateVersion7();

    private static CheckoutView View(
        InvoiceStatus status = InvoiceStatus.AwaitingPayment, DateTimeOffset? graceUntil = null) => new()
    {
        InvoiceId = InvoiceId,
        MerchantName = "Test Shop",
        OrderRef = "ORD-1",
        Amount = 500m,
        ChargedAmount = 500m,
        Currency = "BDT",
        Method = PaymentMethod.Bkash,
        PayToNumber = "01700000000",
        AccountType = WalletAccountType.PersonalRetail,
        Status = status,
        ExpiresAt = Now.AddMinutes(10),
        GraceUntil = graceUntil ?? Now.AddMinutes(25),
    };

    private static (SubmitClaimService Service, FakeClaimStore Claims) Build(CheckoutView? view = null)
    {
        var invoices = new FakeInvoiceStore { Checkout = view ?? View() };
        var claims = new FakeClaimStore();

        return (new SubmitClaimService(invoices, claims, new FixedClock(Now)), claims);
    }

    [Fact]
    public async Task A_valid_id_is_recorded_as_a_claim()
    {
        var (service, claims) = Build();

        var result = await service.SubmitAsync(InvoiceId, "dhd6eyo3ho", "1.2.3.4");

        Assert.Equal(SubmitClaimOutcome.Accepted, result.Outcome);
        Assert.Single(claims.Claims);
        Assert.Equal("DHD6EYO3HO", claims.Claims[0].NormalisedTrxId);
        Assert.Equal(ClaimState.Pending, claims.Claims[0].State);
    }

    [Fact]
    public async Task Recording_a_claim_never_settles_the_invoice()
    {
        // Ten characters in a box is not a payment. The matcher decides, when a message
        // carrying that id actually arrives from the merchant\u0027s phone.
        var (service, claims) = Build();

        await service.SubmitAsync(InvoiceId, "DHD6EYO3HO", null);

        Assert.All(claims.Claims, c => Assert.Equal(ClaimState.Pending, c.State));
    }

    [Fact]
    public async Task The_raw_text_is_kept_alongside_the_normalised_id()
    {
        // When a payment goes missing the first question is what the customer typed.
        var (service, claims) = Build();

        await service.SubmitAsync(InvoiceId, "  dhd6 eyo3 ho ", null);

        Assert.Equal("dhd6 eyo3 ho", claims.Claims[0].SubmittedText);
        Assert.Equal("DHD6EYO3HO", claims.Claims[0].NormalisedTrxId);
    }

    [Fact]
    public async Task Pressing_the_button_twice_is_not_an_error()
    {
        var (service, claims) = Build();

        await service.SubmitAsync(InvoiceId, "DHD6EYO3HO", null);
        var second = await service.SubmitAsync(InvoiceId, "DHD6EYO3HO", null);

        Assert.Equal(SubmitClaimOutcome.AlreadySubmitted, second.Outcome);
        Assert.True(second.Succeeded);
        Assert.Single(claims.Claims);
    }

    [Fact]
    public async Task Gibberish_is_refused_before_anything_is_written()
    {
        var (service, claims) = Build();

        var result = await service.SubmitAsync(InvoiceId, "i paid already", null);

        Assert.Equal(SubmitClaimOutcome.Malformed, result.Outcome);
        Assert.Empty(claims.Claims);
    }

    [Fact]
    public async Task Claims_are_taken_during_the_grace_period_not_just_the_payment_window()
    {
        // The customer paid at the last second and is typing the id a minute later. Their
        // money has already left their wallet.
        var (service, _) = Build(View(graceUntil: Now.AddMinutes(5)));

        var result = await service.SubmitAsync(InvoiceId, "DHD6EYO3HO", null);

        Assert.Equal(SubmitClaimOutcome.Accepted, result.Outcome);
    }

    [Fact]
    public async Task Once_the_grace_period_has_passed_the_page_says_so()
    {
        var (service, _) = Build(View(graceUntil: Now.AddMinutes(-1)));

        var result = await service.SubmitAsync(InvoiceId, "DHD6EYO3HO", null);

        Assert.Equal(SubmitClaimOutcome.NotAcceptingClaims, result.Outcome);
    }

    [Fact]
    public async Task A_paid_invoice_takes_no_more_claims()
    {
        var (service, _) = Build(View(InvoiceStatus.Paid));

        var result = await service.SubmitAsync(InvoiceId, "DHD6EYO3HO", null);

        Assert.Equal(SubmitClaimOutcome.NotAcceptingClaims, result.Outcome);
    }

    [Fact]
    public async Task The_box_is_not_somewhere_to_sit_and_guess()
    {
        var (service, claims) = Build();

        for (var i = 0; i < SubmitClaimService.MaxAttemptsPerInvoice; i++)
        {
            await service.SubmitAsync(InvoiceId, $"AAAAAAAA{i:D2}", null);
        }

        var result = await service.SubmitAsync(InvoiceId, "ZZZZZZZZZZ", null);

        Assert.Equal(SubmitClaimOutcome.TooManyAttempts, result.Outcome);
        Assert.Equal(SubmitClaimService.MaxAttemptsPerInvoice, claims.Claims.Count);
    }
}
