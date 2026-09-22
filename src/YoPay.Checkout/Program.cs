using YoPay.Infrastructure;

// Hosted payment page shown to the customer. Pages land in T5.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddYoPayPersistence(builder.Configuration);
builder.Services.AddYoPaySecurity(builder.Configuration);

var app = builder.Build();

// Liveness only. A readiness probe that checks the database lands with the rest of the
// health wiring in T4, once there is something worth failing over.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "YoPay.Checkout" }));

app.Run();
