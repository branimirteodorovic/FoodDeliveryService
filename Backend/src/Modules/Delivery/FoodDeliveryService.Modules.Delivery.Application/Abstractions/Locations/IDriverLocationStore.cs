using FoodDeliveryService.Modules.Delivery.Domain.Shared;

namespace FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;

/// <summary>
/// Where live driver positions live. Deliberately an Application abstraction rather than a
/// repository: positions are not domain state (they have no invariants and nothing transitions on
/// them), they arrive far too often for the aggregate/outbox path, and the backing store is a
/// swappable infrastructure choice — Redis GEO today, Cosmos behind the same interface in
/// Milestone G.
/// </summary>
public interface IDriverLocationStore
{
    /// <summary>Records a position report. Also refreshes the driver's freshness window — a
    /// driver who stops calling this drops out of the candidate pool.</summary>
    Task RecordAsync(Guid driverId, GeoCoordinate location, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>The driver's last known position, or null if they have never reported one or have
    /// gone stale.</summary>
    Task<DriverLocation?> GetCurrentAsync(Guid driverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Available drivers within radiusKm of origin, nearest first, capped at limit. Excludes
    /// drivers whose last report has gone stale, even if they are still nominally in the pool.
    /// </summary>
    Task<IReadOnlyCollection<NearbyDriver>> FindNearestAvailableAsync(
        GeoCoordinate origin,
        double radiusKm,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Makes the driver a candidate again, at their last known position. A no-op when
    /// they have no fresh position — their next report enrolls them.</summary>
    Task EnterAvailablePoolAsync(Guid driverId, CancellationToken cancellationToken = default);

    /// <summary>Removes the driver from the candidate pool — they went offline, or were reserved
    /// for a delivery. Their last known position stays readable for tracking.</summary>
    Task LeaveAvailablePoolAsync(Guid driverId, CancellationToken cancellationToken = default);
}
