using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Drivers;

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("drivers");

        builder.HasKey(d => d.Id);

        // The id IS the Users service's UserId — supplied by provisioning, never generated here.
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Email).HasMaxLength(300);

        builder.Property(d => d.FirstName).HasMaxLength(200);

        builder.Property(d => d.LastName).HasMaxLength(200);
    }
}
