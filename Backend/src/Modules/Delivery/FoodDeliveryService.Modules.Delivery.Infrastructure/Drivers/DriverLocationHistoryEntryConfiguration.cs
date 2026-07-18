using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Drivers;

internal sealed class DriverLocationHistoryEntryConfiguration : IEntityTypeConfiguration<DriverLocationHistoryEntry>
{
    public void Configure(EntityTypeBuilder<DriverLocationHistoryEntry> builder)
    {
        builder.ToTable("driver_location_history");

        builder.HasKey(e => e.Id);

        // The dominant query is "this driver's track over time" (Feature 2.2's map, Feature 3.4's
        // fraud check), so index by driver + time. Time-partitioning this table is the scaling
        // lever once volume grows — noted in the plan, out of scope here.
        builder.HasIndex(e => new { e.DriverId, e.RecordedOnUtc });
    }
}
