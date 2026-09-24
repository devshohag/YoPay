using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YoPay.Domain.Entities;

namespace YoPay.Infrastructure.Persistence.Configurations;

public sealed class RawEventConfiguration : IEntityTypeConfiguration<RawEvent>
{
    /// <summary>
    /// A note for whoever reaches for declarative partitioning here, because the two
    /// obvious good ideas are mutually exclusive.
    ///
    /// PostgreSQL requires every unique index on a partitioned table to contain the
    /// partition key. Partition this table by month and the global unique index on
    /// dedupe_hash cannot exist - and that index is the only thing making at-least-once
    /// upload from the phones safe. Losing it to gain partitioning trades correctness
    /// for a performance problem this table does not have: a hundred merchants at two
    /// hundred events a day is about seven million rows a year, which Postgres handles
    /// without noticing.
    ///
    /// So: unpartitioned, with retention and a BRIN index on the append-only timestamp.
    /// When the volume genuinely calls for partitioning, deduplication moves to its own
    /// small unpartitioned table first. Do that in that order or the safety net goes.
    /// </summary>
    public void Configure(EntityTypeBuilder<RawEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("raw_events");
        builder.Property(x => x.SenderId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.DedupeHash).HasMaxLength(64).IsRequired();

        // Long enough for an exception's type and message, short enough that nobody is
        // tempted to put a stack trace in the database.
        builder.Property(x => x.FailureReason).HasMaxLength(500);

        // The safety net. Same message uploaded ten times, stored once.
        builder.HasIndex(x => x.DedupeHash).IsUnique();

        builder.HasIndex(x => new { x.State, x.ServerReceivedAt });

        // Append-only and always queried by range, which is exactly what BRIN is for:
        // a fraction of the size of a btree on the same column.
        builder.HasIndex(x => x.ServerReceivedAt).HasMethod("brin");

        builder.HasIndex(x => x.DeviceReceivedAt);

        builder.HasOne(x => x.Device)
            .WithMany()
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ParsedTransactionConfiguration : IEntityTypeConfiguration<ParsedTransaction>
{
    public void Configure(EntityTypeBuilder<ParsedTransaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("parsed_transactions");
        builder.Property(x => x.TrxId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SenderMsisdn).HasMaxLength(20);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.BalanceAfter).HasPrecision(18, 2);
        builder.Property(x => x.Confidence).HasPrecision(3, 2);

        // One provider transaction exists once in this system, full stop.
        builder.HasIndex(x => new { x.Method, x.TrxId }).IsUnique();

        // The matcher's hot path: open sessions for this wallet with this amount.
        builder.HasIndex(x => new { x.WalletId, x.Amount, x.OccurredAt });

        builder.HasOne(x => x.RawEvent)
            .WithMany()
            .HasForeignKey(x => x.RawEventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Template)
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class ParserTemplateConfiguration : IEntityTypeConfiguration<ParserTemplate>
{
    public void Configure(EntityTypeBuilder<ParserTemplate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("parser_templates");
        builder.Property(x => x.SenderId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Pattern).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.FieldMapJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.BaseConfidence).HasPrecision(3, 2);
        builder.Property(x => x.Description).HasMaxLength(500);

        builder.HasIndex(x => new { x.Method, x.SenderId, x.IsActive });
        builder.HasIndex(x => new { x.Method, x.SenderId, x.Version }).IsUnique();
    }
}
