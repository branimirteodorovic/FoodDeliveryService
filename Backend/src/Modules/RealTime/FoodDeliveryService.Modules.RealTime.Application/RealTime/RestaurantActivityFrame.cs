namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The server→client payload for the <c>RestaurantActivity</c> hub method — a single entry on a
/// restaurant manager's live dashboard feed (new order arrived, status changed). Reuses the same
/// <c>status</c> vocabulary as <see cref="OrderStatusFrame"/> (<see cref="OrderStatuses"/>) so the
/// two surfaces stay consistent; this shape is part of the feature's public API, keep it
/// additive-only.
/// </summary>
public sealed record RestaurantActivityFrame(Guid OrderId, string Status, DateTime OccurredOnUtc);
