using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderRejected;

// A restaurant refusing an order. Counted separately from a cancellation because it says something
// about the customer only indirectly — a customer whose orders keep being rejected is a signal, but
// it is not the customer who acted.
public sealed record RecordOrderRejectedCommand(
    Guid OrderId,
    Guid CustomerId,
    DateTime RejectedOnUtc) : ICommand;
