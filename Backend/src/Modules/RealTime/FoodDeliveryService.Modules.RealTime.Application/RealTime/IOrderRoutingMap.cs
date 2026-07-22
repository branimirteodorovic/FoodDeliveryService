namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The ephemeral order→customer/restaurant routing map, backed by Redis at <c>rt:order:{orderId}</c>.
/// It is pure fan-out routing state, not a source of truth: it carries a generous TTL (an order's
/// active window) and is rebuilt from the next status event if lost. Milestone B writes it on every
/// status transition; Milestone C reads it to bind a driver to the customer whose order they carry.
/// </summary>
public interface IOrderRoutingMap
{
    /// <summary>Upserts the routing row for an order, refreshing its TTL.</summary>
    Task SetAsync(Guid orderId, OrderRoutingEntry entry, CancellationToken cancellationToken = default);
}
