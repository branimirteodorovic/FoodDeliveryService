namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The server→client payload for the <c>DriverLocationChanged</c> hub method — the moving pin on
/// the customer's tracking screen. Part of the feature's public API; keep it additive-only. Sourced
/// from Delivery's Redis pub/sub location stream, never RabbitMQ (plan §4.1/§4.3).
/// </summary>
public sealed record DriverLocationFrame(
    Guid OrderId,
    Guid DriverId,
    double Latitude,
    double Longitude,
    DateTime RecordedOnUtc);
