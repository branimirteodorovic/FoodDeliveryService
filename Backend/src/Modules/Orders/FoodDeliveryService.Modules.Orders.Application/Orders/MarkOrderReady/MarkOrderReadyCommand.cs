using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderReady;

public sealed record MarkOrderReadyCommand(Guid OrderId) : ICommand;
