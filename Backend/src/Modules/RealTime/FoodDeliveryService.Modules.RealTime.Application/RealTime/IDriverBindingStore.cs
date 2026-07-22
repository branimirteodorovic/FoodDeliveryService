namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The driver→customer binding the location subscriber consults on every position report (plan
/// §4.2/§4.3). Pure fan-out routing state, same nature as <see cref="IOrderRoutingMap"/>: ephemeral,
/// last-write-wins, rebuilt from the next <c>DriverAssignedIntegrationEvent</c> if ever lost.
/// </summary>
public interface IDriverBindingStore
{
    /// <summary>Binds a driver to the order/customer they are currently carrying, refreshing the
    /// binding's TTL. Called on <c>DriverAssignedIntegrationEvent</c>.</summary>
    Task BindAsync(Guid driverId, Guid orderId, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>The order/customer a driver is currently bound to, or <c>null</c> if unbound
    /// (never assigned, or the order already finished) — a location frame for an unbound driver is
    /// dropped by the subscriber.</summary>
    Task<DriverBinding?> GetAsync(Guid driverId, CancellationToken cancellationToken = default);

    /// <summary>Clears the binding for an order's driver (looked up by order, since the events that
    /// end a delivery — notably <c>OrderCancelledIntegrationEvent</c> — do not all carry a driver
    /// id). Called on <c>OrderDeliveredIntegrationEvent</c>/<c>OrderCancelledIntegrationEvent</c>.
    /// A no-op if the order was never bound.</summary>
    Task UnbindAsync(Guid orderId, CancellationToken cancellationToken = default);
}
