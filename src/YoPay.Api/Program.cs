using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using YoPay.Api.Auth;
using YoPay.Api.Endpoints;
using YoPay.Application.Invoicing;
using YoPay.Application.Security;
using YoPay.Infrastructure;

// Merchant-facing REST API.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddYoPayPersistence(builder.Configuration);
builder.Services.AddYoPaySecurity(builder.Configuration);

builder.Services.AddScoped<MerchantContext>();
builder.Services.AddScoped<CreateInvoiceService>();

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

app.UseRateLimiter();
app.UseMiddleware<SignedRequestMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "YoPay.Api" }));
app.MapPaymentEndpoints(app.Configuration);

app.Run();

/// <summary>Exposed so an integration test host can reach the entry point.</summary>
public partial class Program;
