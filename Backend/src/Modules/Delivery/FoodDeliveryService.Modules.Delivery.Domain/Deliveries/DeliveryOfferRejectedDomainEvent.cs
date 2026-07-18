using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

public sealed class DeliveryOfferRejectedDomainEvent(
    Guid deliveryId,
    Guid orderId,
    Guid driverId) : DomainEvent
{
    public Guid DeliveryId { get; init; } = deliveryId;

    public Guid OrderId { get; init; } = orderId;

    public Guid DriverId { get; init; } = driverId;
}
