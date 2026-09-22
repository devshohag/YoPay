using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YoPay.Domain.Entities;

namespace YoPay.Infrastructure.Persistence.Configurations;

public sealed class DevicePairingTokenConfiguration : IEntityTypeConfiguration<DevicePairingToken>
{
    public void Configure(EntityTypeBuilder<DevicePairingToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("device_pairing_tokens");
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();

        // Single use is enforced here, not by a read-then-write in the service: two
        // handsets scanning the same QR in the same second must not both pair.
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
    }
}
