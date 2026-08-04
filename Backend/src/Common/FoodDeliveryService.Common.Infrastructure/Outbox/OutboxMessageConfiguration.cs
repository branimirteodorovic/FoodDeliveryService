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
    }
}
