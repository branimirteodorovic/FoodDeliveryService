using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

// Transition events carry customer + restaurant ids so the Milestone D integration events can be
// full snapshots (hard rule #9) without re-reading the order.
public sealed class OrderAcceptedDomainEvent(
    Guid orderId,
    Guid customerId,
    Guid restaurantId,
    DateTime acceptedOnUtc) : DomainEvent
{
    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public Guid RestaurantId { get; init; } = restaurantId;

    public DateTime AcceptedOnUtc { get; init; } = acceptedOnUtc;
}
