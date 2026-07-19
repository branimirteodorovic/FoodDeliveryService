using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Delivery.IntegrationEvents;

// Published when the assigned driver completes the delivery. Orders consumes it and advances the
// order to Delivered — the only caller of Order.MarkDelivered(). Full snapshot (hard rule #9).
public sealed class OrderDeliveredIntegrationEvent : IntegrationEvent
{
    public OrderDeliveredIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid orderId,
        Guid deliveryId,
        Guid driverId,
        DateTime deliveredOnUtc)
        : base(id, occurredOnUtc)
    {
        OrderId = orderId;
        DeliveryId = deliveryId;
        DriverId = driverId;
        DeliveredOnUtc = deliveredOnUtc;
    }

    public Guid OrderId { get; init; }

    public Guid DeliveryId { get; init; }

    public Guid DriverId { get; init; }

    public DateTime DeliveredOnUtc { get; init; }
}
