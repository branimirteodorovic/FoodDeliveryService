using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.AcceptOrder;

public sealed record AcceptOrderCommand(Guid OrderId) : ICommand;
