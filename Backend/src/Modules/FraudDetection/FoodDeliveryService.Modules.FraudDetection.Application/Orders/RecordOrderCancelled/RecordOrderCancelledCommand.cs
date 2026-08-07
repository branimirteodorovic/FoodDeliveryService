using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderCancelled;

// The event Milestone B's two customer signals are built on. Note what it does NOT carry: who
// cancelled, and the status at cancellation. The second is reconstructed from FraudDetection's own OrderFact
// (see OrderFact.MarkCancelled); the first is the additive upstream change Milestone B decides on.
public sealed record RecordOrderCancelledCommand(
    Guid OrderId,
    Guid CustomerId,
    DateTime CancelledOnUtc) : ICommand;
