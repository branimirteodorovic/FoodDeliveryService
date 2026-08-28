using FoodDeliveryService.Modules.Support.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Audit;

internal sealed class SupportAuditEntryConfiguration : IEntityTypeConfiguration<SupportAuditEntry>
{
    public void Configure(EntityTypeBuilder<SupportAuditEntry> builder)
    {
        builder.ToTable("support_audit_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.FromValue).HasMaxLength(SupportAuditEntry.ValueMaxLength);
        builder.Property(e => e.ToValue).HasMaxLength(SupportAuditEntry.ValueMaxLength);
        builder.Property(e => e.Reason).HasMaxLength(SupportAuditEntry.ReasonMaxLength);

        // The only way this table is ever read: one ticket's history, newest first. A composite
        // index over exactly that, so the audit endpoint never scans the whole log.
        builder.HasIndex(e => new { e.TicketId, e.OccurredOnUtc });

        // No foreign key to tickets on purpose. A cascade is the one thing that could delete an
        // audit row, and an append-only log must have no delete path at all — not even a transitive
        // one. The ticket's existence is checked in the read instead.
    }
}
