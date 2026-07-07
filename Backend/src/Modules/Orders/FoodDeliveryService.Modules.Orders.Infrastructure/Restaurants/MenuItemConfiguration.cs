using FoodDeliveryService.Modules.Orders.Domain.Restaurants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Restaurants;

internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("menu_items");

        // Id IS the Restaurants service's MenuItemId (replica) — never generated locally.
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Name).HasMaxLength(200);

        builder.Property(i => i.Price).HasPrecision(10, 2);

        // Placement loads all requested items of one restaurant in a single batch lookup.
        builder.HasIndex(i => i.RestaurantId);
    }
}
