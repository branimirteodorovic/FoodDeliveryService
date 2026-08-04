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
    }
}
