using FoodDeliveryService.Modules.Support.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Tickets;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // The reference is what a customer quotes on the phone, so it has to identify exactly one
        // ticket. The unique index is also the last line of defence if the sequence is ever reset.
        builder.Property(t => t.Reference).HasMaxLength(20);
        builder.HasIndex(t => t.Reference).IsUnique();

        builder.Property(t => t.Subject).HasMaxLength(Ticket.SubjectMaxLength);

        // The agent queue: "open and unassigned, newest first" and every status/agent filter the
        // list endpoint offers land on one of these two.
        builder.HasIndex(t => new { t.Status, t.OpenedOnUtc });
        builder.HasIndex(t => t.AssignedAgentId);

        // A customer's own list, and the ownership predicate on every single-ticket read.
        builder.HasIndex(t => t.CustomerId);

        // The thread is part of this aggregate, so it is configured from the parent's side. Cascade
        // is nominal — nothing deletes a ticket — but leaving the default would make the FK optional
        // and permit an orphan message, which is a row no read in this module would ever return.
        builder.HasMany(t => t.Messages)
            .WithOne()
            .HasForeignKey(m => m.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // Messages exposes a defensive copy; EF must track the backing field.
        builder.Navigation(t => t.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Reserved for the AI assistant escalation transcript. jsonb rather than text because that
        // is the shape it will arrive in, and changing a column type later costs a table rewrite.
        builder.Property(t => t.EscalationTranscript).HasColumnType("jsonb");
    }
}
