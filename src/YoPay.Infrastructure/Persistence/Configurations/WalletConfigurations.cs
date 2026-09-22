using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YoPay.Domain.Entities;

namespace YoPay.Infrastructure.Persistence.Configurations;

public sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("wallets");
        builder.Property(x => x.Number).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(100);
        builder.Property(x => x.DeclaredMonthlyLimit).HasPrecision(18, 2);

        builder.HasIndex(x => new { x.MerchantId, x.Method, x.Number }).IsUnique();

        builder.HasOne(x => x.Merchant)
            .WithMany(x => x.Wallets)
            .HasForeignKey(x => x.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("devices");
        builder.Property(x => x.Fingerprint).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PublicKey).HasMaxLength(512);
        builder.Property(x => x.Model).HasMaxLength(100);
        builder.Property(x => x.AppVersion).HasMaxLength(32);
        builder.Property(x => x.NetworkType).HasMaxLength(32);

        builder.HasIndex(x => x.Fingerprint).IsUnique();

        // The ops console opens on "which devices have gone quiet", so that query gets
        // an index rather than a scan over every device.
        builder.HasIndex(x => x.LastHeartbeatAt);

        builder.HasOne(x => x.Wallet)
            .WithMany(x => x.Devices)
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
