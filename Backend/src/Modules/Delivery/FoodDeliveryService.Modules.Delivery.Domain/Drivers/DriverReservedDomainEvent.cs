using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Drivers;

public sealed class DriverReservedDomainEvent(Guid driverId) : DomainEvent
{
    public Guid DriverId { get; init; } = driverId;
}
