using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Consumers;

// One direct consumer per Orders lifecycle event. Each is intentionally trivial — the fan-out body
// lives in OrderStatusConsumer<T>; a subclass only names its event and its mapping. Kept together in
// one file because each is three lines and they form a single cohesive set.

internal sealed class OrderPlacedConsumer(IRealTimeNotifier notifier, IOrderRoutingMap routingMap)
    : OrderStatusConsumer<OrderPlacedIntegrationEvent>(notifier, routingMap)
{
    protected override (OrderStatusFrame Frame, Guid CustomerId, Guid RestaurantId) Map(OrderPlacedIntegrationEvent message) =>
        (OrderStatusFrame.From(message), message.CustomerId, message.RestaurantId);
}

internal sealed class OrderAcceptedConsumer(IRealTimeNotifier notifier, IOrderRoutingMap routingMap)
    : OrderStatusConsumer<OrderAcceptedIntegrationEvent>(notifier, routingMap)
{
    protected override (OrderStatusFrame Frame, Guid CustomerId, Guid RestaurantId) Map(OrderAcceptedIntegrationEvent message) =>
        (OrderStatusFrame.From(message), message.CustomerId, message.RestaurantId);
}

internal sealed class OrderRejectedConsumer(IRealTimeNotifier notifier, IOrderRoutingMap routingMap)
    : OrderStatusConsumer<OrderRejectedIntegrationEvent>(notifier, routingMap)
{
    protected override (OrderStatusFrame Frame, Guid CustomerId, Guid RestaurantId) Map(OrderRejectedIntegrationEvent message) =>
        (OrderStatusFrame.From(message), message.CustomerId, message.RestaurantId);
}

internal sealed class OrderReadyForPickupConsumer(IRealTimeNotifier notifier, IOrderRoutingMap routingMap)
    : OrderStatusConsumer<OrderReadyForPickupIntegrationEvent>(notifier, routingMap)
{
    protected override (OrderStatusFrame Frame, Guid CustomerId, Guid RestaurantId) Map(OrderReadyForPickupIntegrationEvent message) =>
        (OrderStatusFrame.From(message), message.CustomerId, message.RestaurantId);
}

internal sealed class OrderCancelledConsumer(IRealTimeNotifier notifier, IOrderRoutingMap routingMap)
    : OrderStatusConsumer<OrderCancelledIntegrationEvent>(notifier, routingMap)
{
    protected override (OrderStatusFrame Frame, Guid CustomerId, Guid RestaurantId) Map(OrderCancelledIntegrationEvent message) =>
        (OrderStatusFrame.From(message), message.CustomerId, message.RestaurantId);
}
