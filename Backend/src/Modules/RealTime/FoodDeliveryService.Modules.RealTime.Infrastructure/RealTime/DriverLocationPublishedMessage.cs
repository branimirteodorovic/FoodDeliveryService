namespace FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime;

/// <summary>
/// The wire shape read off the <c>delivery:driver-locations</c> Redis channel. Mirrors Delivery's
/// own <c>DriverLocationPublishedMessage</c> field-for-field — deliberately not a shared contracts
/// project (this is not an integration event; it never touches RabbitMQ or the outbox, plan §4.1).
/// </summary>
internal sealed record DriverLocationPublishedMessage(Guid DriverId, double Latitude, double Longitude, DateTime RecordedOnUtc);
