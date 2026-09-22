using YoPay.Infrastructure;

// Device-only ingest endpoint, deliberately a separate host so a flood of device traffic cannot take the merchant API down with it. Endpoints land in T6.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddYoPayPersistence(builder.Configuration);

var app = builder.Build();

// Liveness only. A readiness probe that checks the database lands with the rest of the
// health wiring in T4, once there is something worth failing over.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "YoPay.Ingest.Api" }));

app.Run();
