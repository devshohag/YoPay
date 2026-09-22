using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YoPay.Infrastructure.Persistence;
using YoPay.Infrastructure.Persistence.Interceptors;

namespace YoPay.Infrastructure;

public static class DependencyInjection
{
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

        return services;
    }
}
