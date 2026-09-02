using FoodDeliveryService.Modules.Support.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Orders;

internal sealed class OrderSnapshotConfiguration : IEntityTypeConfiguration<OrderSnapshot>
{
    public void Configure(EntityTypeBuilder<OrderSnapshot> builder)
    {
        builder.ToTable("order_snapshots");

        builder.HasKey(o => o.Id);

        // The key is the Orders service's OrderId, carried in on the event — never generated here.
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.Subtotal).HasPrecision(18, 2);

        // "This customer's recent orders" is the read the ticket context is built on, so the index
        // is here from the first migration rather than added once the table is large.
        builder.HasIndex(o => o.CustomerId);
    }
}
