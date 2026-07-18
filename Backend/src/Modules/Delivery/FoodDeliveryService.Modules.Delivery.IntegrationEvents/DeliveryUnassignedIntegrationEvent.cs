using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Delivery.IntegrationEvents;

// Every candidate within the radius was tried without an accept — the delivery is parked for
// admin/support attention.
public sealed class DeliveryUnassignedIntegrationEvent : IntegrationEvent
{
    public DeliveryUnassignedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid deliveryId,
        Guid orderId)
        : base(id, occurredOnUtc)
    {
        DeliveryId = deliveryId;
        OrderId = orderId;
    }

    public Guid DeliveryId { get; init; }

    public Guid OrderId { get; init; }
}
