using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.StartPreparingOrder;

public sealed record StartPreparingOrderCommand(Guid OrderId) : ICommand;
