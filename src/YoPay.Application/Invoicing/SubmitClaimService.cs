using YoPay.Application.Abstractions;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;

namespace YoPay.Application.Invoicing;

/// <summary>
/// Records what a customer says they paid.
///
/// Deliberately does not decide anything. It writes a claim and returns; the matcher
/// settles the invoice when a message carrying that transaction id actually arrives from
/// the merchant's phone. A checkout page that marked an invoice paid because someone
/// typed ten characters into a box would be a free shop.
/// </summary>
public sealed class SubmitClaimService(IInvoiceStore invoices, IClaimStore claims, IClock clock)
{
    /// <summary>
    /// Enough for a customer who mistypes twice and pastes the wrong message once, and
    /// few enough that the box is not a place to sit and guess transaction ids.
    /// </summary>
    public const int MaxAttemptsPerInvoice = 5;

    public async Task<SubmitClaimResult> SubmitAsync(
        Guid invoiceId,
        string? typed,
        string? clientIp,
        CancellationToken ct = default)
    {
        var normalised = TrxIdInput.Normalise(typed);
        if (normalised is null)
        {
            return new SubmitClaimResult(SubmitClaimOutcome.Malformed);
        }

        var invoice = await invoices.FindCheckoutAsync(invoiceId, ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return new SubmitClaimResult(SubmitClaimOutcome.InvoiceNotFound);
        }

        if (!AcceptsClaims(invoice, clock.UtcNow))
        {
            return new SubmitClaimResult(SubmitClaimOutcome.NotAcceptingClaims);
        }

        if (await claims.ExistsAsync(invoiceId, normalised, ct).ConfigureAwait(false))
        {
            // The customer pressed the button twice, or refreshed. Not an error.
            return new SubmitClaimResult(SubmitClaimOutcome.AlreadySubmitted, normalised);
        }

        if (await claims.CountForInvoiceAsync(invoiceId, ct).ConfigureAwait(false) >= MaxAttemptsPerInvoice)
        {
            return new SubmitClaimResult(SubmitClaimOutcome.TooManyAttempts);
        }

        await claims.AddAsync(
            new PaymentClaim
            {
                InvoiceId = invoiceId,
                SubmittedText = typed!.Trim(),
                NormalisedTrxId = normalised,
                SubmittedAt = clock.UtcNow,
                ClientIp = clientIp,
                State = ClaimState.Pending,
            },
            ct).ConfigureAwait(false);

        return new SubmitClaimResult(SubmitClaimOutcome.Accepted, normalised);
    }

    /// <summary>
    /// Claims are taken right up to the end of the grace period, not the payment window.
    /// A customer who paid at the last second is still typing the id a minute later, and
    /// their money has already left their wallet.
    /// </summary>
    public static bool AcceptsClaims(CheckoutView invoice, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return invoice.Status is InvoiceStatus.AwaitingPayment
                   or InvoiceStatus.PendingReview
                   or InvoiceStatus.Partial
               && now <= invoice.GraceUntil;
    }
}
