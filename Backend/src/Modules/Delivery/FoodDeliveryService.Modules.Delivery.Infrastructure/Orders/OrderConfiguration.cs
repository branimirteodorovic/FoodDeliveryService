using FoodDeliveryService.Modules.Delivery.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Orders;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        // Id IS the Orders service's OrderId (replica) — never generated locally.
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.HasIndex(o => o.CustomerId);
        builder.HasIndex(o => o.RestaurantId);

        builder.OwnsOne(o => o.DeliveryAddress, addressBuilder =>
        {
            addressBuilder.Property(a => a.Street).HasMaxLength(300).HasColumnName("delivery_street");
            addressBuilder.Property(a => a.City).HasMaxLength(200).HasColumnName("delivery_city");
            addressBuilder.Property(a => a.PostalCode).HasMaxLength(20).HasColumnName("delivery_postal_code");
            addressBuilder.Property(a => a.Country).HasMaxLength(100).HasColumnName("delivery_country");
            addressBuilder.Property(a => a.Notes).HasMaxLength(500).HasColumnName("delivery_notes");
            addressBuilder.Property(a => a.Latitude).HasColumnName("delivery_latitude");
            addressBuilder.Property(a => a.Longitude).HasColumnName("delivery_longitude");
        });
    }
}
