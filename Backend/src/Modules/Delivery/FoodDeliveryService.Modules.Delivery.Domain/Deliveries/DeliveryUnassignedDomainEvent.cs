using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

public sealed class DeliveryUnassignedDomainEvent(Guid deliveryId, Guid orderId) : DomainEvent
{
    public Guid DeliveryId { get; init; } = deliveryId;

    public Guid OrderId { get; init; } = orderId;
}
