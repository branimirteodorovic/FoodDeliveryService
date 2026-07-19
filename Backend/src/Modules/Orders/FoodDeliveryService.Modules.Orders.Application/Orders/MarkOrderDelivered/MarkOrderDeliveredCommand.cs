using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderDelivered;

// Driven only by the Delivery service's OrderDelivered event (via the inbox) — no endpoint exposes
// it. Advances the order OutForDelivery → Delivered; DeliveredOnUtc is sourced from the event.
public sealed record MarkOrderDeliveredCommand(Guid OrderId, DateTime DeliveredOnUtc) : ICommand;
