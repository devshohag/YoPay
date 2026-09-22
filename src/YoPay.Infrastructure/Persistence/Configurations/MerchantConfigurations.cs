using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YoPay.Domain.Entities;

namespace YoPay.Infrastructure.Persistence.Configurations;

public sealed class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("merchants");
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ContactEmail).HasMaxLength(320).IsRequired();
        builder.Property(x => x.ContactMsisdn).HasMaxLength(20);

        builder.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class ApiCredentialConfiguration : IEntityTypeConfiguration<ApiCredential>
{
    public void Configure(EntityTypeBuilder<ApiCredential> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("api_credentials");
        builder.Property(x => x.KeyId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ApiKeyHash).HasMaxLength(200).IsRequired();
        builder.Property(x => x.HmacSecretEncrypted).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(100);
        builder.Property(x => x.LastUsedIp).HasMaxLength(45);

        builder.HasIndex(x => x.KeyId).IsUnique();
        builder.HasIndex(x => new { x.MerchantId, x.KeyVersion });

        builder.HasOne(x => x.Merchant)
            .WithMany(x => x.Credentials)
            .HasForeignKey(x => x.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RequestNonceConfiguration : IEntityTypeConfiguration<RequestNonce>
{
    public void Configure(EntityTypeBuilder<RequestNonce> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("request_nonces");
        builder.Property(x => x.KeyId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Nonce).HasMaxLength(128).IsRequired();

        // The whole point of the table: one signature, one use.
        builder.HasIndex(x => new { x.KeyId, x.Nonce }).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("subscriptions");
        builder.HasIndex(x => new { x.MerchantId, x.Status });

        builder.HasOne(x => x.Merchant)
            .WithMany()
            .HasForeignKey(x => x.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
