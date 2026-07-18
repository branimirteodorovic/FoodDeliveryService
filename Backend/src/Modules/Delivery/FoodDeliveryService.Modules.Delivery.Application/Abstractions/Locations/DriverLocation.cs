using FoodDeliveryService.Modules.Delivery.Domain.Shared;

namespace FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;

/// <summary>A driver's last known position, as read back from the location store.</summary>
public sealed record DriverLocation(Guid DriverId, GeoCoordinate Location, DateTime RecordedOnUtc);
