using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderAccepted;

// Restaurant acceptance. Nothing on the customer row moves — the value of this event to FraudDetection is
// entirely that it puts the order into the state a later cancellation is measured against.
public sealed record RecordOrderAcceptedCommand(Guid OrderId, DateTime AcceptedOnUtc) : ICommand;
