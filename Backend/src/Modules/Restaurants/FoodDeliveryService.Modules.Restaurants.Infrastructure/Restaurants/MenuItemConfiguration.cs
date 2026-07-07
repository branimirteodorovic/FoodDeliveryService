using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Restaurants;

internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("menu_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Name).HasMaxLength(200);

        builder.Property(i => i.Description).HasMaxLength(1000);

        builder.Property(i => i.Price).HasPrecision(10, 2);

        builder.Property(i => i.PhotoUrl).HasMaxLength(1000);

        // Denormalized restaurant id lets GetMenu read all items in one query without joining
        // through categories.
        builder.HasIndex(i => i.RestaurantId);
    }
}
