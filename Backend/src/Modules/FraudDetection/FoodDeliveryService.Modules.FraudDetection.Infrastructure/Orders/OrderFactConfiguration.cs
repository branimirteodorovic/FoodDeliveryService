using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure.Orders;

internal sealed class OrderFactConfiguration : IEntityTypeConfiguration<OrderFact>
{
    public void Configure(EntityTypeBuilder<OrderFact> builder)
    {
        builder.ToTable("order_facts");

        // Id IS the Orders service's OrderId (projection) — never generated locally.
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.Subtotal).HasPrecision(10, 2);

        // Every rolling-window signal in Milestone B is "this customer's orders since T", so the
        // composite index is on exactly that pair rather than on CustomerId alone.
        builder.HasIndex(o => new { o.CustomerId, o.PlacedOnUtc });

        builder.HasIndex(o => o.RestaurantId);

        // Milestone D looks up an order by the delivery that completed it.
        builder.HasIndex(o => o.DeliveryId);

        // Milestone F buckets orders per hour off this column.
        builder.HasIndex(o => o.PlacedOnUtc);
    }
}
