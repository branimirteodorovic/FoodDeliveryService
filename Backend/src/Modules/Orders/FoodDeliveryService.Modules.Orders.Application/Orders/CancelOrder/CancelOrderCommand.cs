using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.CancelOrder;

public sealed record CancelOrderCommand(Guid OrderId) : ICommand;
