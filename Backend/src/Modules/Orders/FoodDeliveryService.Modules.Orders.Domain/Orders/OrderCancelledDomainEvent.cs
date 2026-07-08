using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

public sealed class OrderCancelledDomainEvent(
    Guid orderId,
    Guid customerId,
    Guid restaurantId,
    DateTime cancelledOnUtc) : DomainEvent
{
    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public Guid RestaurantId { get; init; } = restaurantId;

    public DateTime CancelledOnUtc { get; init; } = cancelledOnUtc;
}
