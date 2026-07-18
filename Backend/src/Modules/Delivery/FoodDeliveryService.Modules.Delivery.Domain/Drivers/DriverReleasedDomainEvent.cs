using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Drivers;

public sealed class DriverReleasedDomainEvent(Guid driverId) : DomainEvent
{
    public Guid DriverId { get; init; } = driverId;
}
