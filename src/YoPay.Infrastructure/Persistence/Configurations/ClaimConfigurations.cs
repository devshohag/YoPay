using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YoPay.Domain.Entities;

namespace YoPay.Infrastructure.Persistence.Configurations;

public sealed class PaymentClaimConfiguration : IEntityTypeConfiguration<PaymentClaim>
{
    public void Configure(EntityTypeBuilder<PaymentClaim> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payment_claims");

        // Kept as typed, capped because it may be a whole pasted SMS.
        builder.Property(x => x.SubmittedText).HasMaxLength(500).IsRequired();
        builder.Property(x => x.NormalisedTrxId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ClientIp).HasMaxLength(45);

        // One claim per id per invoice: a customer refreshing the page should not fill
        // the table, and the attempt counter should count attempts, not refreshes.
        builder.HasIndex(x => new { x.InvoiceId, x.NormalisedTrxId }).IsUnique();

        // The matcher's lookup when an invoice is in transaction-id mode.
        builder.HasIndex(x => new { x.NormalisedTrxId, x.State });

        builder.HasOne(x => x.Invoice)
            .WithMany()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
