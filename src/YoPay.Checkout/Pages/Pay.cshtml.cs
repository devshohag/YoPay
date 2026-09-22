using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using YoPay.Application.Invoicing;

namespace YoPay.Checkout.Pages;

public class PayModel(IInvoiceStore invoices, SubmitClaimService claims) : PageModel
{
    public CheckoutView? Invoice { get; private set; }

    [BindProperty]
    public string? TrxId { get; set; }

    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool Submitted { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid invoiceId, CancellationToken ct)
    {
        Invoice = await invoices.FindCheckoutAsync(invoiceId, ct).ConfigureAwait(false);

        return Invoice is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid invoiceId, CancellationToken ct)
    {
        Invoice = await invoices.FindCheckoutAsync(invoiceId, ct).ConfigureAwait(false);
        if (Invoice is null)
        {
            return NotFound();
        }

        var result = await claims
            .SubmitAsync(invoiceId, TrxId, HttpContext.Connection.RemoteIpAddress?.ToString(), ct)
            .ConfigureAwait(false);

        // Every message says what to do next. "Invalid input" on a payment page, with
        // someone's money already sent, is not an answer.
        (Message, IsError, Submitted) = result.Outcome switch
        {
            SubmitClaimOutcome.Accepted or SubmitClaimOutcome.AlreadySubmitted =>
                ("ধন্যবাদ। আমরা পেমেন্টটি যাচাই করছি — এই পেজটি খোলা রাখুন। " +
                 "Thanks. We are verifying your payment; keep this page open.", false, true),

            SubmitClaimOutcome.Malformed =>
                ("TrxID টি ১০ অক্ষরের হয়, যেমন DHD6EYO3HO। পুরো SMS টিও পেস্ট করতে পারেন। " +
                 "A TrxID is 10 characters, e.g. DHD6EYO3HO. You can paste the whole SMS instead.",
                 true, false),

            SubmitClaimOutcome.NotAcceptingClaims =>
                ("এই ইনভয়েসের সময় শেষ হয়ে গেছে। দোকানিকে জানান, টাকা কাটা হয়ে থাকলে ফেরত পাবেন। " +
                 "This invoice has closed. Contact the seller; if money left your wallet it will be returned.",
                 true, false),

            SubmitClaimOutcome.TooManyAttempts =>
                ("অনেকবার চেষ্টা হয়েছে। দোকানির সাথে যোগাযোগ করুন। " +
                 "Too many attempts. Please contact the seller.", true, false),

            _ => ("কিছু একটা ভুল হয়েছে। আবার চেষ্টা করুন। Something went wrong. Please try again.",
                  true, false),
        };

        return Page();
    }
}
