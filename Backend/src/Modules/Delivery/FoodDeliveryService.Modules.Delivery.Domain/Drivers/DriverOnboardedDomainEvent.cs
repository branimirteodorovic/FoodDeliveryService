using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Drivers;

public sealed class DriverOnboardedDomainEvent(Guid driverId) : DomainEvent
{
    public Guid DriverId { get; init; } = driverId;
}
