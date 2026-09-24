using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YoPay.Infrastructure.Persistence;

/// <summary>
/// Builds the model for the EF tooling without starting a host. Migrations can then be
/// added and applied without the API, Redis or any other dependency being up.
/// </summary>
public sealed class YoPayDbContextFactory : IDesignTimeDbContextFactory<YoPayDbContext>
{
    /// <summary>
    /// The local development database, exactly as docker-compose.yml starts it and exactly
    /// as appsettings.json reaches it.
    ///
    /// It used to say Password=design-time-only, on the theory that a design-time fallback
    /// should not carry a working password. The result was that every `dotnet ef` command
    /// run without the environment variable failed with
    /// "28P01: password authentication failed for user yopay" - which reads as a broken
    /// database or a wrong password, sends you to pg_hba.conf, and is none of those things.
    /// A default that lies about the failure costs more than it protects.
    ///
    /// Nothing is exposed by this: the password is the compose file's default for a
    /// container on the developer's own machine. Every other environment sets
    /// ConnectionStrings__Postgres, which still wins.
    /// </summary>
    private const string DesignTimeConnection =
        "Host=localhost;Port=5433;Database=yopay;Username=yopay;Password=yopay";

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
