using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Refunds;

internal sealed class RefundRequestConfiguration : IEntityTypeConfiguration<RefundRequest>
{
    /// <summary>
    /// At most one live refund per order. The handler's pre-check produces the clean business
    /// failure; this index is what actually holds when two agents on two tickets for the same order
    /// pass that check at the same instant, because no aggregate here carries a concurrency token.
    /// <para>
    /// Partial, on the two non-terminal-for-this-purpose statuses: Requested (0) and Approved (1).
    /// A rejected request must not block a better-argued second attempt.
    /// </para>
    /// </summary>
    internal const string ActiveRefundPerOrderFilter = "status IN (0, 1)";

    public void Configure(EntityTypeBuilder<RefundRequest> builder)
    {
        builder.ToTable("refund_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // Money, so an explicit scale rather than whatever the provider defaults to: a refund
        // silently rounded by the database is the one rounding nobody would think to look for.
        builder.Property(r => r.Amount).HasPrecision(18, 2);

        // Copied from the ticket at creation and never updated — see RefundRequest.TicketReference
        // for why a denormalized copy is the right call for this one field.
        builder.Property(r => r.TicketReference).HasMaxLength(20);

        builder.Property(r => r.Reason).HasMaxLength(RefundRequest.ReasonMaxLength);
        builder.Property(r => r.DecisionNote).HasMaxLength(RefundRequest.DecisionNoteMaxLength);

        // A foreign key without a navigation property: the refund is its own aggregate, and a
        // navigation would invite loading one through the other. Restrict rather than Cascade on
        // purpose — a refund decision records what a human agreed to, so no delete anywhere may
        // take one with it as a side effect. Same reasoning as the audit log, which goes further
        // and declares no key at all.
        builder
            .HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(r => r.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.Status);

        builder
            .HasIndex(r => r.OrderId)
            .IsUnique()
            .HasFilter(ActiveRefundPerOrderFilter);
    }
}
