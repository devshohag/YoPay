using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YoPay.Domain.Entities;

namespace YoPay.Infrastructure.Persistence.Configurations;

public sealed class PaymentMatchConfiguration : IEntityTypeConfiguration<PaymentMatch>
{
    public void Configure(EntityTypeBuilder<PaymentMatch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payment_matches");
        builder.Property(x => x.OperatorId).HasMaxLength(100);

        // These two indexes are the product's correctness guarantee. Everything above
        // them - the advisory lock, the single transaction, the idempotency check - is
        // there to avoid hitting them. When something slips through anyway, the database
        // refuses, and the worst case is a retry instead of a double-paid order.
        builder.HasIndex(x => x.InvoiceId).IsUnique();
        builder.HasIndex(x => x.ParsedTransactionId).IsUnique();

        builder.HasOne(x => x.Invoice)
            .WithMany()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ParsedTransaction)
            .WithMany()
            .HasForeignKey(x => x.ParsedTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FraudSignalConfiguration : IEntityTypeConfiguration<FraudSignal>
{
    public void Configure(EntityTypeBuilder<FraudSignal> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("fraud_signals");
        builder.Property(x => x.TrxIdHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasIndex(x => new { x.Method, x.TrxIdHash }).IsUnique();
    }
}
