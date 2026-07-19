using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderOutForDelivery;

// Driven only by the Delivery service's OrderPickedUp event (via the inbox) — no endpoint exposes
// it. Advances the order ReadyForPickup → OutForDelivery.
public sealed record MarkOrderOutForDeliveryCommand(Guid OrderId) : ICommand;
