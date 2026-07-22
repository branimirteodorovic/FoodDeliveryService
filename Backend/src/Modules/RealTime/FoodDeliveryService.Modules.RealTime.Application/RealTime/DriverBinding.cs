namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The ephemeral driver→order→customer binding stored at <c>rt:driver:{driverId}</c>. Written when
/// a driver is assigned to an order (so the location subscriber can resolve who to push a position
/// to) and cleared when that order is delivered or cancelled, so later stray positions for the same
/// driver are silently dropped instead of leaking into a finished order's timeline.
/// </summary>
public sealed record DriverBinding(Guid OrderId, Guid CustomerId);
