using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.RejectOrder;

public sealed record RejectOrderCommand(Guid OrderId, string Reason) : ICommand;
