using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Database.RestaurantManagers;

internal sealed class RestaurantManagerConfiguration : IEntityTypeConfiguration<RestaurantManager>
{
    public void Configure(EntityTypeBuilder<RestaurantManager> builder)
    {
        builder.ToTable("restaurant_managers");

        // Id IS the Users service's UserId (replica) — never generated locally.
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.RestaurantName).HasMaxLength(200);

        // RestaurantAddressUpdatedIntegrationEvent carries no ManagerUserId, so the rename path looks
        // up rows by RestaurantId instead of the primary key — index it for that lookup.
        builder.HasIndex(m => m.RestaurantId);
    }
}
