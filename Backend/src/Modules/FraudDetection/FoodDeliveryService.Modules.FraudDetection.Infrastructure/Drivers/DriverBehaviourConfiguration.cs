using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure.Drivers;

internal sealed class DriverBehaviourConfiguration : IEntityTypeConfiguration<DriverBehaviour>
{
    public void Configure(EntityTypeBuilder<DriverBehaviour> builder)
    {
        builder.ToTable("driver_behaviours");

        // Id IS the driver's UserId (projection) — never generated locally.
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.HasIndex(d => d.LastDeliveryOnUtc);
    }
}
