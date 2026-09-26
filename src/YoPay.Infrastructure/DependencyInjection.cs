using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YoPay.Application.Abstractions;
using YoPay.Application.Devices;
using YoPay.Application.Ingestion;
using YoPay.Application.Matching;
using YoPay.Application.Invoicing;
using YoPay.Application.Security;
using YoPay.Application.Webhooks;
using YoPay.Infrastructure.Matching;
using YoPay.Infrastructure.Webhooks;
using YoPay.Infrastructure.Persistence;
using YoPay.Infrastructure.Persistence.Interceptors;
using YoPay.Infrastructure.Security;

namespace YoPay.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Named client for anything a merchant supplied the address of.</summary>
    public const string OutboundHttpClient = "yopay-outbound";

    public static IServiceCollection AddYoPayPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres is not configured.");

        services.AddSingleton<TimestampInterceptor>();

        services.AddDbContext<YoPayDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", YoPayDbContext.Schema);
                npgsql.EnableRetryOnFailure(3);
            });

            options.AddInterceptors(provider.GetRequiredService<TimestampInterceptor>());
        });

        services.AddScoped<IInvoiceStore, EfInvoiceStore>();
        services.AddScoped<IClaimStore, EfClaimStore>();
        services.AddScoped<IDeviceStore, EfDeviceStore>();
        services.AddScoped<IRawEventStore, EfRawEventStore>();
        services.AddScoped<IPipelineStore, EfPipelineStore>();
        services.AddScoped<IPaymentMatcher, EfPaymentMatcher>();
        services.AddScoped<IWebhookStore, EfWebhookStore>();
        services.AddScoped<IWebhookEndpointStore, EfWebhookEndpointStore>();

        return services;
    }

    /// <summary>
    /// Secret vault, replay defence and the outbound client.
    ///
    /// The HTTP client is registered here and nowhere else on purpose. Every request this
    /// platform makes to an address a merchant chose has to go through the connect
    /// callback; a second HttpClient built somewhere else is a server-side request forgery
    /// hole with no warning attached to it.
    /// </summary>
    public static IServiceCollection AddYoPaySecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var secretOptions = SecurityConfiguration.ReadSecretProtection(configuration);

        services.AddSingleton(secretOptions);
        services.AddSingleton<ISecretProtector>(_ => new AesGcmSecretProtector(secretOptions));
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<INonceStore, EfNonceStore>();
        services.AddScoped<ICredentialLookup, EfCredentialLookup>();
        services.AddScoped<SignatureVerifier>();

        // One switch, read once, used by both halves of the guard - the URL check when a
        // merchant saves an endpoint and the connect callback when the dispatcher dials
        // it. Two separate settings would eventually disagree, and the half that stayed
        // on would silently stop mattering.
        var allowPrivateEndpoints =
            configuration.GetValue<bool>(WebhookOptions.AllowPrivateEndpointsKey);

        services.AddSingleton(new WebhookOptions { AllowPrivateEndpoints = allowPrivateEndpoints });

        services.AddHttpClient(OutboundHttpClient, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("YoPay-Webhook/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(
                () => OutboundConnect.CreateHandler(allowPrivateEndpoints));

        // Registered beside the client it depends on, so the two cannot drift apart.
        services.AddScoped<IWebhookSender, HttpWebhookSender>();

        return services;
    }
}
