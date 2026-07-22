using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Consumers;

// One direct consumer per Milestone C Delivery lifecycle event. The fan-out body lives in
// DeliveryStatusConsumer<T>; each subclass only names its event, its mapping, and (where needed)
// the driver-binding side effect.

internal sealed class DriverAssignedConsumer(
    IRealTimeNotifier notifier,
    IOrderRoutingMap routingMap,
    IDriverBindingStore driverBindingStore,
    ILogger<DriverAssignedConsumer> logger)
    : DeliveryStatusConsumer<DriverAssignedIntegrationEvent>(notifier, routingMap, logger)
{
    protected override Guid GetOrderId(DriverAssignedIntegrationEvent message) => message.OrderId;

    protected override OrderStatusFrame MapFrame(DriverAssignedIntegrationEvent message) =>
        OrderStatusFrame.From(message);

    // Binds the driver so the location subscriber (Milestone C) can resolve this customer on every
    // position report from here until the order is delivered or cancelled.
    protected override Task AfterFanOutAsync(DriverAssignedIntegrationEvent message, Guid customerId, CancellationToken cancellationToken) =>
        driverBindingStore.BindAsync(message.DriverId, message.OrderId, customerId, cancellationToken);
}

internal sealed class OrderPickedUpConsumer(
    IRealTimeNotifier notifier,
    IOrderRoutingMap routingMap,
    ILogger<OrderPickedUpConsumer> logger)
    : DeliveryStatusConsumer<OrderPickedUpIntegrationEvent>(notifier, routingMap, logger)
{
    protected override Guid GetOrderId(OrderPickedUpIntegrationEvent message) => message.OrderId;

    protected override OrderStatusFrame MapFrame(OrderPickedUpIntegrationEvent message) =>
        OrderStatusFrame.From(message);
}

internal sealed class OrderDeliveredConsumer(
    IRealTimeNotifier notifier,
    IOrderRoutingMap routingMap,
    IDriverBindingStore driverBindingStore,
    ILogger<OrderDeliveredConsumer> logger)
    : DeliveryStatusConsumer<OrderDeliveredIntegrationEvent>(notifier, routingMap, logger)
{
    protected override Guid GetOrderId(OrderDeliveredIntegrationEvent message) => message.OrderId;

    protected override OrderStatusFrame MapFrame(OrderDeliveredIntegrationEvent message) =>
        OrderStatusFrame.From(message);

    // The delivery is over — clear the binding so a straggling location report for this driver is
    // dropped rather than reaching a customer whose order already finished.
    protected override Task AfterFanOutAsync(OrderDeliveredIntegrationEvent message, Guid customerId, CancellationToken cancellationToken) =>
        driverBindingStore.UnbindAsync(message.OrderId, cancellationToken);
}
