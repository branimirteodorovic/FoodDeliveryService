using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Delivery.IntegrationEvents;

// Carries the driver's name and vehicle so Notifications can send "your driver is Alex" without
// calling back (hard rule #9).
public sealed class DriverAssignedIntegrationEvent : IntegrationEvent
{
    public DriverAssignedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid orderId,
        Guid deliveryId,
        Guid driverId,
        string driverFirstName,
        string driverLastName,
        string vehicleType,
        DateTime assignedOnUtc)
        : base(id, occurredOnUtc)
    {
        OrderId = orderId;
        DeliveryId = deliveryId;
        DriverId = driverId;
        DriverFirstName = driverFirstName;
        DriverLastName = driverLastName;
        VehicleType = vehicleType;
        AssignedOnUtc = assignedOnUtc;
    }

    public Guid OrderId { get; init; }

    public Guid DeliveryId { get; init; }

    public Guid DriverId { get; init; }

    public string DriverFirstName { get; init; }

    public string DriverLastName { get; init; }

    public string VehicleType { get; init; }

    public DateTime AssignedOnUtc { get; init; }
}
