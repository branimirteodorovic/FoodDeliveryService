namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The fan-out surface the bus consumers depend on, so they broadcast through an interface rather
/// than binding to SignalR types directly (the <c>IHubContext</c> implementation lives in
/// Infrastructure). A frame is sent to exactly one customer's <c>user:{userId}</c> group, never
/// broadcast — group isolation is the whole point.
/// </summary>
public interface IRealTimeNotifier
{
    /// <summary>
    /// Broadcasts an order-status frame to the customer's own group. Best-effort: any transport
    /// failure is immaterial (the client re-syncs from the REST read models on reconnect), so
    /// implementations swallow-and-log rather than throw back onto the bus.
    /// </summary>
    Task NotifyUserAsync(Guid userId, OrderStatusFrame frame, CancellationToken cancellationToken = default);
}
