namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Locations;

/// <summary>
/// The wire shape published to the <c>delivery:driver-locations</c> Redis channel. Deliberately not
/// an <c>IntegrationEvent</c> — this never touches RabbitMQ or the outbox (Feature 2.2 plan §4.1).
/// The RealTime service's subscriber deserializes the identical shape independently; there is no
/// shared contracts project for this pub/sub message, so keep the two definitions in sync by hand.
/// </summary>
internal sealed record DriverLocationPublishedMessage(Guid DriverId, double Latitude, double Longitude, DateTime RecordedOnUtc);
