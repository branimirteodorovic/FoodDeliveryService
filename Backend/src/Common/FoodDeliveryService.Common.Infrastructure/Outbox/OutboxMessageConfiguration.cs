using FoodDeliveryService.Common.Infrastructure.Correlation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Common.Infrastructure.Outbox;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Content).HasMaxLength(2000).HasColumnType("jsonb");

        builder.Property(o => o.CorrelationId).HasMaxLength(MessageCorrelationColumns.CorrelationIdMaxLength);

        builder.Property(o => o.TraceParent).HasMaxLength(MessageCorrelationColumns.TraceParentMaxLength);

        // "Which outbox rows belong to this correlation id?" is a support question in its own right,
        // and the only reason the id is a column instead of a field inside the serialized content.
        // Partial, because the answer is always about a specific id and never about the nulls.
        builder
            .HasIndex(o => o.CorrelationId)
            .HasFilter("correlation_id IS NOT NULL");

        // The dispatch query — `WHERE processed_on_utc IS NULL ORDER BY occurred_on_utc LIMIT n`,
        // run by every module's ProcessOutboxJob every five seconds, forever.
        //
        // Without this index it is a sequential scan of the whole table, and the whole table is
        // *history*: rows are never deleted, so the cost of finding the next twenty messages grows
        // with every message the module has ever published. Measured during Feature 3.5 Milestone F
        // on the Delivery outbox at 32,958 rows — 99.99% of them already processed — the scan read
        // 2,567 shared buffers and took 16.0 ms; with this index it reads 1 buffer and takes
        // 0.125 ms. That is 128× on a query no amount of load makes cheaper and no idle period ever
        // shrinks.
        //
        // Partial on the same predicate, which is what keeps it honest: the index only ever holds
        // the *unprocessed* rows, so it is a few kilobytes on a 22 MB table, an UPDATE that sets
        // processed_on_utc deletes the entry rather than updating it, and the index cannot itself
        // become the thing that grows without bound.
        builder
            .HasIndex(o => o.OccurredOnUtc)
            .HasFilter("processed_on_utc IS NULL")
            .HasDatabaseName("ix_outbox_messages_unprocessed");
    }
}
