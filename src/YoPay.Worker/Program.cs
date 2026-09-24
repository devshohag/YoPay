using YoPay.Application.Matching;
using YoPay.Application.Parsing;
using YoPay.Infrastructure;
using YoPay.Worker;

// Parser, matcher and - from T9 - the webhook dispatcher.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddYoPayPersistence(builder.Configuration);
builder.Services.AddYoPaySecurity(builder.Configuration);

builder.Services.AddSingleton<IMessageParser>(_ => MessageParser.ForBkash());
builder.Services.AddScoped<PaymentPipeline>();
builder.Services.AddHostedService<PaymentPipelineWorker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "YoPay.Worker" }));

app.Run();
