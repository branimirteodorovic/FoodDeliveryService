using FoodDeliveryService.Modules.Delivery.Domain.Restaurants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Restaurants;

internal sealed class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        builder.ToTable("restaurants");

        // Id IS the Restaurants service's RestaurantId (replica) — never generated locally.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Name).HasMaxLength(300);

        builder.Property(r => r.Latitude);

        builder.Property(r => r.Longitude);
    }
}
