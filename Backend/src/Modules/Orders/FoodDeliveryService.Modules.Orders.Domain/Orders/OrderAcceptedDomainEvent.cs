using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

// Transition events carry customer + restaurant ids so the Milestone D integration events can be
// full snapshots (hard rule #9) without re-reading the order.
//
// PreviousStatus is the status the order moved OUT of. Only the aggregate knows it — by the time a
// handler sees the event the order has already advanced — and for Cancel it genuinely varies
// (Pending or Accepted). It is what gives the orders.state_transition counter (Telemetry 2.4
// Milestone B) an honest `from` tag instead of one hard-coded from the transition table.
public sealed class OrderAcceptedDomainEvent(
    Guid orderId,
    Guid customerId,
    Guid restaurantId,
    OrderStatus previousStatus,
    DateTime acceptedOnUtc) : DomainEvent
{
    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public Guid RestaurantId { get; init; } = restaurantId;

    public OrderStatus PreviousStatus { get; init; } = previousStatus;

    public DateTime AcceptedOnUtc { get; init; } = acceptedOnUtc;
}
