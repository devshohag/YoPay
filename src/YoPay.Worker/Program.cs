using YoPay.Application.Matching;
using YoPay.Application.Parsing;
using YoPay.Infrastructure;
using YoPay.Worker;
using YoPay.Application.Webhooks;

// Two loops in one host: the payment pipeline and the webhook dispatcher.
//
// Separate hosted services rather than one, because they fail differently. A merchant's
// server hanging for thirty seconds must not delay the settlement of somebody else's
// payment, and a parser exception must not stop notifications going out. The outbox table
// is the seam between them.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddYoPayPersistence(builder.Configuration);
builder.Services.AddYoPaySecurity(builder.Configuration);

builder.Services.AddSingleton<IMessageParser>(_ => MessageParser.ForBkash());
builder.Services.AddScoped<PaymentPipeline>();
builder.Services.AddHostedService<PaymentPipelineWorker>();

builder.Services.AddScoped<WebhookPipeline>();
builder.Services.AddHostedService<WebhookDispatchWorker>();

var app = builder.Build();

// Said out loud, every start, so an instance running with the guard loosened cannot do so
// quietly. A setting nobody can see is a setting nobody turns back off.
if (app.Configuration.GetValue<bool>(WebhookOptions.AllowPrivateEndpointsKey))
{
    app.Logger.LogWarning("{Warning}", WebhookOptions.Warning);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "YoPay.Worker" }));

app.Run();
