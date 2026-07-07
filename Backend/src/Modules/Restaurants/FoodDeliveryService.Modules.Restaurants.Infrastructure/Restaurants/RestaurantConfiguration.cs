using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Restaurants;

internal sealed class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        builder.ToTable("restaurants");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(300);

        builder.Property(r => r.TaxIdentification).HasMaxLength(100);

        builder.Property(r => r.CuisineType).HasMaxLength(100);

        builder.Property(r => r.Email).HasMaxLength(300);

        builder.Property(r => r.PhoneNumber).HasMaxLength(50);

        // Fraction in [0, 1) — 4 decimal places allow rates like 0.1275 (12.75%).
        builder.Property(r => r.CommissionRate).HasPrecision(5, 4);

        // Not unique — one manager may run multiple restaurants.
        builder.HasIndex(r => r.ManagerUserId);

        builder.OwnsOne(r => r.Address, addressBuilder =>
        {
            addressBuilder.Property(a => a.Street).HasMaxLength(300).HasColumnName("address_street");
            addressBuilder.Property(a => a.City).HasMaxLength(200).HasColumnName("address_city");
            addressBuilder.Property(a => a.PostalCode).HasMaxLength(20).HasColumnName("address_postal_code");
            addressBuilder.Property(a => a.Country).HasMaxLength(100).HasColumnName("address_country");
            addressBuilder.Property(a => a.Latitude).HasColumnName("address_latitude");
            addressBuilder.Property(a => a.Longitude).HasColumnName("address_longitude");
        });

        builder.HasMany(r => r.MenuCategories)
            .WithOne()
            .HasForeignKey(c => c.RestaurantId)
            .OnDelete(DeleteBehavior.Cascade);

        // MenuCategories exposes a defensive copy; EF must track the backing field.
        builder.Navigation(r => r.MenuCategories).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
