using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

public sealed class OrderRejectedDomainEvent(
    Guid orderId,
    Guid customerId,
    Guid restaurantId,
    string reason,
    DateTime rejectedOnUtc) : DomainEvent
{
    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public Guid RestaurantId { get; init; } = restaurantId;

    public string Reason { get; init; } = reason;

    public DateTime RejectedOnUtc { get; init; } = rejectedOnUtc;
}
