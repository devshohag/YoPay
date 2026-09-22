using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YoPay.Infrastructure.Persistence;

/// <summary>
/// Builds the model for the EF tooling without starting a host. Migrations can then be
/// added and applied without the API, Redis or any other dependency being up.
/// </summary>
public sealed class YoPayDbContextFactory : IDesignTimeDbContextFactory<YoPayDbContext>
{
    private const string DesignTimeConnection =
        "Host=localhost;Port=5433;Database=yopay;Username=yopay;Password=design-time-only";

    public YoPayDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? DesignTimeConnection;

        var options = new DbContextOptionsBuilder<YoPayDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", YoPayDbContext.Schema))
            .Options;

        return new YoPayDbContext(options);
    }
}
