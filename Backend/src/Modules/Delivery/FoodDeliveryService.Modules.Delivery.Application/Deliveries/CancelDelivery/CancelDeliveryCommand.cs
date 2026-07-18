using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.CancelDelivery;

// Compensation for OrderCancelledIntegrationEvent — keyed by OrderId because that is all the
// Orders service knows. Inbox-driven and idempotent: no delivery yet (order cancelled before
// ready) and an already-terminal delivery both settle as success.
public sealed record CancelDeliveryCommand(Guid OrderId) : ICommand;
