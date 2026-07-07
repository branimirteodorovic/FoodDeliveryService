using FoodDeliveryService.Modules.Orders.Domain.Restaurants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Restaurants;

internal sealed class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        builder.ToTable("restaurants");

        // Id IS the Restaurants service's RestaurantId (replica) — never generated locally.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Name).HasMaxLength(300);

        builder.Property(r => r.CommissionRate).HasPrecision(5, 4);
    }
}
