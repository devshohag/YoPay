using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using YoPay.Application.Invoicing;
using YoPay.Infrastructure;

// The page the customer sees. Server rendered, no framework, no build step: it loads on a
// bad connection on a cheap phone, which is the only device that matters here.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddYoPayPersistence(builder.Configuration);
builder.Services.AddYoPaySecurity(builder.Configuration);

builder.Services.AddScoped<SubmitClaimService>();
builder.Services.AddRazorPages();

// This surface is public and unauthenticated, so the limit is per address. It is high
// enough that a shared connection in an office does not trip it and low enough that the
// transaction id box is not somewhere to sit and guess.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("checkout", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

app.UseRateLimiter();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "YoPay.Checkout" }));

// Polled by the page every few seconds. Returns the status and nothing else - it is
// reachable by anyone holding the invoice id, so it says only what that page already
// shows.
app.MapGet("/pay/{invoiceId:guid}/status", async (
        Guid invoiceId, IInvoiceStore store, CancellationToken ct) =>
    {
        var view = await store.FindCheckoutAsync(invoiceId, ct).ConfigureAwait(false);

        return view is null
            ? Results.NotFound()
            : Results.Ok(new
            {
                status = view.Status.ToString(),
                settled = view.IsSettled,
                redirectUrl = view.IsSettled ? view.RedirectUrl : null,
            });
    })
    .RequireRateLimiting("checkout");

app.MapRazorPages().RequireRateLimiting("checkout");

app.Run();
