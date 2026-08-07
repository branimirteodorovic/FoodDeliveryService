using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordOrderDelivered;

// From the Delivery service, and the event Milestone D's location-mismatch check hangs off: it is
// the moment the retained position trail is compared against the drop-off recorded on the fact.
public sealed record RecordOrderDeliveredCommand(
    Guid OrderId,
    Guid DeliveryId,
    Guid DriverId,
    DateTime DeliveredOnUtc) : ICommand;
