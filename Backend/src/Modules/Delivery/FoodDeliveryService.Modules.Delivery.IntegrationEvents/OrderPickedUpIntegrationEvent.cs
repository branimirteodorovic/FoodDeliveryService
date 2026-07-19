using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Delivery.IntegrationEvents;

// Published when the assigned driver collects the food. Orders consumes it and advances the order
// to OutForDelivery — the only caller of Order.MarkOutForDelivery(). Full snapshot (hard rule #9).
public sealed class OrderPickedUpIntegrationEvent : IntegrationEvent
{
    public OrderPickedUpIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid orderId,
        Guid deliveryId,
        Guid driverId,
        DateTime pickedUpOnUtc)
        : base(id, occurredOnUtc)
    {
        OrderId = orderId;
        DeliveryId = deliveryId;
        DriverId = driverId;
        PickedUpOnUtc = pickedUpOnUtc;
    }

    public Guid OrderId { get; init; }

    public Guid DeliveryId { get; init; }

    public Guid DriverId { get; init; }

    public DateTime PickedUpOnUtc { get; init; }
}
