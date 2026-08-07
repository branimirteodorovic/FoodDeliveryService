using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordOrderPickedUp;

// From the Delivery service. Binds a driver to the order for the first time — everything the driver
// signals need about who handled which order starts here.
public sealed record RecordOrderPickedUpCommand(
    Guid OrderId,
    Guid DeliveryId,
    Guid DriverId,
    DateTime PickedUpOnUtc) : ICommand;
