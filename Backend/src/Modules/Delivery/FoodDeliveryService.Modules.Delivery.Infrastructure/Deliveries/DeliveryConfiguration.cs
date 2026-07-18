using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Deliveries;

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<DeliveryAggregate>
{
    public void Configure(EntityTypeBuilder<DeliveryAggregate> builder)
    {
        builder.ToTable("deliveries");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        // One delivery per order — and the DB-level idempotency guard for a redelivered
        // OrderReadyForPickup event racing itself.
        builder.HasIndex(d => d.OrderId).IsUnique();

        // ProcessExpiredOffersJob scans WHERE status = Offered AND offer_expires_on_utc < now.
        builder.HasIndex(d => new { d.Status, d.OfferExpiresOnUtc });

        builder.HasIndex(d => d.DriverId);

        builder.OwnsOne(d => d.PickupLocation, locationBuilder =>
        {
            locationBuilder.Property(l => l.Latitude).HasColumnName("pickup_latitude");
            locationBuilder.Property(l => l.Longitude).HasColumnName("pickup_longitude");
        });

        builder.OwnsOne(d => d.DropoffAddress, addressBuilder =>
        {
            addressBuilder.Property(a => a.Street).HasMaxLength(300).HasColumnName("dropoff_street");
            addressBuilder.Property(a => a.City).HasMaxLength(200).HasColumnName("dropoff_city");
            addressBuilder.Property(a => a.PostalCode).HasMaxLength(20).HasColumnName("dropoff_postal_code");
            addressBuilder.Property(a => a.Country).HasMaxLength(100).HasColumnName("dropoff_country");
            addressBuilder.Property(a => a.Notes).HasMaxLength(500).HasColumnName("dropoff_notes");
            addressBuilder.Property(a => a.Latitude).HasColumnName("dropoff_latitude");
            addressBuilder.Property(a => a.Longitude).HasColumnName("dropoff_longitude");
        });

        // The tried-drivers list maps to a uuid[] column (Npgsql array mapping) via its backing
        // field — no separate table for what is a small, append-only set private to the aggregate.
        builder.Property<List<Guid>>("_triedDriverIds")
            .HasColumnName("tried_driver_ids");
    }
}
