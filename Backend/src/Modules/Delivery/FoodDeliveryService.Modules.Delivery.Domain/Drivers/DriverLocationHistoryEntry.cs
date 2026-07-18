using FoodDeliveryService.Modules.Delivery.Domain.Shared;

namespace FoodDeliveryService.Modules.Delivery.Domain.Drivers;

/// <summary>
/// One recorded driver position. Deliberately NOT an aggregate and NOT an Entity — it is an
/// append-only telemetry log with no invariants, no state changes and nothing for another service
/// to react to, so it raises no domain events and never goes through the outbox. Live positions
/// live in Redis; this is the durable history (Feature 3.4's fraud signal reads it).
/// </summary>
public sealed class DriverLocationHistoryEntry
{
    private DriverLocationHistoryEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid DriverId { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public DateTime RecordedOnUtc { get; private set; }

    /// <summary>Set once a position can be attributed to a delivery in progress (Milestone E+);
    /// null while the driver is merely available.</summary>
    public Guid? DeliveryId { get; private set; }

    public static DriverLocationHistoryEntry Record(
        Guid driverId,
        GeoCoordinate location,
        DateTime utcNow,
        Guid? deliveryId = null)
    {
        ArgumentNullException.ThrowIfNull(location);

        return new DriverLocationHistoryEntry
        {
            Id = Guid.NewGuid(),
            DriverId = driverId,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            RecordedOnUtc = utcNow,
            DeliveryId = deliveryId
        };
    }
}
