using FoodDeliveryService.Modules.Support.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Tickets;

internal sealed class TicketMessageConfiguration : IEntityTypeConfiguration<TicketMessage>
{
    public void Configure(EntityTypeBuilder<TicketMessage> builder)
    {
        builder.ToTable("ticket_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Body).HasMaxLength(TicketMessage.BodyMaxLength);

        // The only way this table is read: one ticket's thread in posting order.
        builder.HasIndex(m => new { m.TicketId, m.PostedOnUtc });
    }
}
