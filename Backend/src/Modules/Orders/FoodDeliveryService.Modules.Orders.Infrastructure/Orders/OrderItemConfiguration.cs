using FoodDeliveryService.Modules.Orders.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Orders;

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Name).HasMaxLength(200);

        builder.Property(i => i.UnitPrice).HasPrecision(10, 2);

        builder.Property(i => i.LineTotal).HasPrecision(10, 2);

        // No FK to the menu_items replica — the id is a cross-service reference; the replica row
        // may arrive later or change independently of placed orders.
        builder.HasIndex(i => i.MenuItemId);
    }
}
