using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using YoPay.Application.Devices;
using YoPay.Application.Ingestion;
using YoPay.Contracts.Ingest;
using YoPay.Infrastructure;
using YoPay.Infrastructure.Persistence;
using YoPay.Ingest.Api.Auth;

// Device-only ingest. A separate host from the merchant API on purpose: a flood of
// handset traffic, or a bug in this surface, must not take the merchant API down with it.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddYoPayPersistence(builder.Configuration);
builder.Services.AddYoPaySecurity(builder.Configuration);

builder.Services.AddScoped<DeviceContext>();
builder.Services.AddScoped<DevicePairingService>();
builder.Services.AddScoped<IngestService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Per device. Heartbeats are frequent and batches are occasional, so the limit is
    // generous; what it is really there for is one broken handset in a restart loop.
    options.AddPolicy("device", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Request.Headers[DeviceSignedRequestMiddleware.DeviceHeader]
                          .ToString() is { Length: > 0 } id
            ? $"device:{id}"
            : $"ip:{context.Connection.RemoteIpAddress}",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 240,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // Pairing is unauthenticated, so it is limited hard and by address. Twelve attempts a
    // minute is plenty for someone typing a code and nowhere near enough to search for one.
    options.AddPolicy("pairing", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

app.UseRateLimiter();
app.UseMiddleware<DeviceSignedRequestMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "YoPay.Ingest.Api" }));

app.MapPost("/v1/ingest/pair", async (
        PairDeviceRequest request,
        DevicePairingService pairing,
        YoPayDbContext db,
        CancellationToken ct) =>
    {
        var result = await pairing
            .PairAsync(request.Token, request.PublicKey, request.Model, request.AppVersion, ct)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Results.Problem(
                title: result.Outcome == PairOutcome.InvalidPublicKey
                    ? "The public key is not an EC P-256 SubjectPublicKeyInfo."
                    : "That pairing code cannot be used.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var details = await db.Devices
            .AsNoTracking()
            .Where(d => d.Id == result.DeviceId)
            .Select(d => new
            {
                WalletNumber = db.Wallets.Where(w => w.Id == d.WalletId).Select(w => w.Number).First(),
                MerchantName = db.Merchants.Where(m => m.Id == d.MerchantId).Select(m => m.Name).First(),
            })
            .FirstAsync(ct)
            .ConfigureAwait(false);

        return Results.Ok(new PairDeviceResponse
        {
            DeviceId = result.DeviceId!.Value,
            WalletNumber = details.WalletNumber,
            MerchantName = details.MerchantName,
        });
    })
    .RequireRateLimiting("pairing");

app.MapPost("/v1/ingest/events", async (
        DeviceEventBatch batch,
        DeviceContext device,
        IngestService ingest,
        CancellationToken ct) =>
    {
        if (!device.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        var outcome = await ingest
            .IngestAsync(device.Device!, batch.Events, ct)
            .ConfigureAwait(false);

        return Results.Ok(new DeviceIngestResponse
        {
            Accepted = outcome.Accepted,
            Duplicates = outcome.Duplicates,
        });
    })
    .RequireRateLimiting("device");

app.MapPost("/v1/ingest/heartbeat", async (
        DeviceHeartbeat heartbeat,
        DeviceContext device,
        IDeviceStore devices,
        CancellationToken ct) =>
    {
        if (!device.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        await devices.RecordHeartbeatAsync(
                device.Device!.DeviceId,
                heartbeat.AppVersion,
                heartbeat.PermissionState,
                heartbeat.BatteryPercent,
                heartbeat.NetworkType,
                ct)
            .ConfigureAwait(false);

        return Results.Ok(new { acknowledged = true });
    })
    .RequireRateLimiting("device");

app.Run();
