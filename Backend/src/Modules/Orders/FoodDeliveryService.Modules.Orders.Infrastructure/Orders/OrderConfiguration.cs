using FoodDeliveryService.Modules.Orders.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Orders;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Subtotal).HasPrecision(10, 2);

        // Same shape as the Restaurants side — a fraction in [0, 1) with 4 decimal places.
        builder.Property(o => o.CommissionRate).HasPrecision(5, 4);

        // The unique index is the idempotency guarantee: two concurrent placements with the same
        // key cannot both insert — the loser catches the violation and returns the winner.
        builder.Property(o => o.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(o => o.IdempotencyKey).IsUnique();

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

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Items exposes a defensive copy; EF must track the backing field.
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
