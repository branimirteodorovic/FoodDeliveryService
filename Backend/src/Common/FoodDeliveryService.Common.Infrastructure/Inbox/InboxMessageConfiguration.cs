using FoodDeliveryService.Common.Infrastructure.Correlation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Common.Infrastructure.Inbox;

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Content).HasMaxLength(2000).HasColumnType("jsonb");

        builder.Property(o => o.CorrelationId).HasMaxLength(MessageCorrelationColumns.CorrelationIdMaxLength);

        builder.Property(o => o.TraceParent).HasMaxLength(MessageCorrelationColumns.TraceParentMaxLength);

        // Same reason as the outbox side: the consuming half of "show me everything about this
        // correlation id" has to be answerable without scanning the table.
        builder
            .HasIndex(o => o.CorrelationId)
            .HasFilter("correlation_id IS NOT NULL");

        // The consuming half of the dispatch query, and the same fix for the same reason — see
        // OutboxMessageConfiguration for the measurement. A busy module's inbox grows exactly as
        // fast as everybody else's outboxes publish into it, so this side degrades first in a
        // service that mostly reacts rather than publishes.
        builder
            .HasIndex(o => o.OccurredOnUtc)
            .HasFilter("processed_on_utc IS NULL")
            .HasDatabaseName("ix_inbox_messages_unprocessed");
    }
}
