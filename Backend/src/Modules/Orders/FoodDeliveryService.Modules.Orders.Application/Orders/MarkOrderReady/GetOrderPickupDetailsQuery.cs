using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderReady;

// Internal read used only to build the OrderReadyForPickup full snapshot: the order's delivery
// address (incl. coordinates) and subtotal joined with the restaurant replica's pickup coordinates.
public sealed record GetOrderPickupDetailsQuery(Guid OrderId) : IQuery<OrderPickupDetailsResponse>;
