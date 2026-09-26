using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using YoPay.Api.Auth;
using YoPay.Api.Endpoints;
using YoPay.Application.Invoicing;
using YoPay.Application.Security;
using YoPay.Infrastructure;
using YoPay.Application.Webhooks;

// Merchant-facing REST API.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddYoPayPersistence(builder.Configuration);
builder.Services.AddYoPaySecurity(builder.Configuration);

builder.Services.AddScoped<MerchantContext>();
builder.Services.AddScoped<CreateInvoiceService>();
builder.Services.AddScoped<CancelInvoiceService>();
builder.Services.AddRazorPages();

// Partitioned by API key, falling back to the remote address for requests that never got
// as far as authentication. Keyed by IP alone, one noisy merchant behind a shared NAT
// would throttle everyone else behind it.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("merchant", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Request.Headers[SignedRequest.KeyHeader].ToString() is { Length: > 0 } key
            ? $"key:{key}"
            : $"ip:{context.Connection.RemoteIpAddress}",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

// Said out loud, every start, so an instance running with the guard loosened cannot do so
// quietly. A setting nobody can see is a setting nobody turns back off.
if (app.Configuration.GetValue<bool>(WebhookOptions.AllowPrivateEndpointsKey))
{
    app.Logger.LogWarning("{Warning}", WebhookOptions.Warning);
}

app.UseRateLimiter();
app.UseMiddleware<SignedRequestMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "YoPay.Api" }));
app.MapPaymentEndpoints(app.Configuration);
app.MapWebhookEndpoints();

// The development console, and the link the dashboard uses to open a checkout page.
// Mapped only in Development: it has no authentication and reads every merchant's data,
// so the day it is reachable from anywhere but a laptop is the day it becomes the worst
// hole in the product.
if (app.Environment.IsDevelopment())
{
    app.MapRazorPages();

    app.MapGet("/pay-link/{invoiceId:guid}", (Guid invoiceId, IConfiguration configuration) =>
    {
        var baseUrl = configuration["Checkout:BaseUrl"]?.TrimEnd('/') ?? "http://localhost:5082";
        return Results.Redirect($"{baseUrl}/pay/{invoiceId}");
    });
}

app.Run();

/// <summary>Exposed so an integration test host can reach the entry point.</summary>
public partial class Program;
