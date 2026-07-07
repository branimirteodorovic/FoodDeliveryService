using FoodDeliveryService.Modules.Restaurants.Domain.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Managers;

internal sealed class RestaurantManagerConfiguration : IEntityTypeConfiguration<RestaurantManager>
{
    public void Configure(EntityTypeBuilder<RestaurantManager> builder)
    {
        builder.ToTable("restaurant_managers");

        // Id IS the Users service's UserId (replica) — never generated locally.
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Email).HasMaxLength(300);

        builder.Property(m => m.FirstName).HasMaxLength(200);

        builder.Property(m => m.LastName).HasMaxLength(200);
    }
}
