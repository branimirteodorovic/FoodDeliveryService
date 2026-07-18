using FoodDeliveryService.Modules.Delivery.Domain.Shared;

namespace FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;

/// <summary>
/// An available driver within the search radius, with the distance the store computed. Returned
/// distance-ordered — the assignment routine (Milestone E) takes the first candidate it has not
/// already tried.
/// </summary>
public sealed record NearbyDriver(Guid DriverId, GeoCoordinate Location, double DistanceKm);
