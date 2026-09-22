using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YoPay.Domain.Entities;

namespace YoPay.Infrastructure.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("invoices");
        builder.Property(x => x.OrderRef).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.ChargedAmount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(200);
        builder.Property(x => x.CustomerEmail).HasMaxLength(320);
        builder.Property(x => x.CustomerMsisdn).HasMaxLength(20);
        builder.Property(x => x.RedirectUrl).HasMaxLength(2000);
        builder.Property(x => x.CallbackUrl).HasMaxLength(2000);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        // A retried create must not produce a second invoice for the same order.
        builder.HasIndex(x => new { x.MerchantId, x.OrderRef }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.GraceUntil });

        builder.HasOne(x => x.Wallet)
            .WithMany()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentSessionConfiguration : IEntityTypeConfiguration<PaymentSession>
{
    public void Configure(EntityTypeBuilder<PaymentSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payment_sessions");
        builder.Property(x => x.ExpectedAmount).HasPrecision(18, 2);

        // The index that makes an incoming amount unambiguous: within one wallet, at
        // most one open session may expect a given amount. Enforced by Postgres, not by
        // an application check that two workers could pass at the same moment.
        // SessionState.Open = 1.
        builder.HasIndex(x => new { x.WalletId, x.ExpectedAmount })
            .IsUnique()
            .HasFilter("state = 1");

        builder.HasIndex(x => new { x.WalletId, x.State, x.ExpiresAt });

        builder.HasOne(x => x.Invoice)
            .WithMany(x => x.Sessions)
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Wallet)
            .WithMany()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
