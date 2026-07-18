using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

public sealed class DeliveryAssignedDomainEvent(
    Guid deliveryId,
    Guid orderId,
    Guid driverId,
    DateTime assignedOnUtc) : DomainEvent
{
    public Guid DeliveryId { get; init; } = deliveryId;

    public Guid OrderId { get; init; } = orderId;

    public Guid DriverId { get; init; } = driverId;

    public DateTime AssignedOnUtc { get; init; } = assignedOnUtc;
}
