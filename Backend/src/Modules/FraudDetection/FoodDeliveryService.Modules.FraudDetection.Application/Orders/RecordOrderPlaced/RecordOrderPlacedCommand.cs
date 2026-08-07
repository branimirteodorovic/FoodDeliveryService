using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderPlaced;

// Creates the OrderFact row and advances the customer's counters. Inbox-driven: idempotent because
// the fact row already existing means the event has been seen, and the counters move only on the
// insert path.
public sealed record RecordOrderPlacedCommand(
    Guid OrderId,
    Guid CustomerId,
    Guid RestaurantId,
    decimal Subtotal,
    DateTime PlacedOnUtc) : ICommand;
