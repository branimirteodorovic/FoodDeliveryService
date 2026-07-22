namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The ephemeral routing row stored at <c>rt:order:{orderId}</c>: who to fan out a given order's
/// frames to. Written by every status consumer (Milestone B) so the map is warm before a driver is
/// ever assigned; Milestone C reads it to resolve a driver-location frame's target customer.
/// </summary>
public sealed record OrderRoutingEntry(Guid CustomerId, Guid RestaurantId);
