using Microsoft.EntityFrameworkCore;
using YoPay.Domain.Entities;

namespace YoPay.Infrastructure.Persistence;

public class YoPayDbContext(DbContextOptions<YoPayDbContext> options) : DbContext(options)
{
    public const string Schema = "yopay";

    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<ApiCredential> ApiCredentials => Set<ApiCredential>();
    public DbSet<RequestNonce> RequestNonces => Set<RequestNonce>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<PaymentSession> PaymentSessions => Set<PaymentSession>();
    public DbSet<RawEvent> RawEvents => Set<RawEvent>();
    public DbSet<ParsedTransaction> ParsedTransactions => Set<ParsedTransaction>();
    public DbSet<ParserTemplate> ParserTemplates => Set<ParserTemplate>();
    public DbSet<PaymentMatch> PaymentMatches => Set<PaymentMatch>();
    public DbSet<FraudSignal> FraudSignals => Set<FraudSignal>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(YoPayDbContext).Assembly);
        modelBuilder.UseSnakeCaseNames();
    }
}
