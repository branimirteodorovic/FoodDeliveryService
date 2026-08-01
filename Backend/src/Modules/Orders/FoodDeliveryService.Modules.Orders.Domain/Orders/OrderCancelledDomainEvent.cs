using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

public sealed class OrderCancelledDomainEvent(
    Guid orderId,
    Guid customerId,
    Guid restaurantId,
    OrderStatus previousStatus,
    DateTime cancelledOnUtc) : DomainEvent
{
    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public Guid RestaurantId { get; init; } = restaurantId;

    // The one transition with a genuinely variable source: a customer can back out while the order
    // is still Pending or after the restaurant Accepted it, and the two are worth telling apart.
    public OrderStatus PreviousStatus { get; init; } = previousStatus;

    public DateTime CancelledOnUtc { get; init; } = cancelledOnUtc;
}
