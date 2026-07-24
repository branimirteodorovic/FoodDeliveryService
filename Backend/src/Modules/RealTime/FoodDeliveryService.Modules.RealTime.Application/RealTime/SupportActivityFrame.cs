namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The server→client payload for the <c>SupportActivity</c> hub method — a coarse, global feed of
/// every order/delivery transition, for the single <c>support</c> group. Unlike
/// <see cref="OrderStatusFrame"/>/<see cref="RestaurantActivityFrame"/> (each scoped to one
/// customer/restaurant) this carries the restaurant id too, since a support agent has no other way
/// to tell which restaurant an entry belongs to. Part of the feature's public API — additive-only.
/// </summary>
public sealed record SupportActivityFrame(Guid OrderId, Guid RestaurantId, string Status, DateTime OccurredOnUtc);
