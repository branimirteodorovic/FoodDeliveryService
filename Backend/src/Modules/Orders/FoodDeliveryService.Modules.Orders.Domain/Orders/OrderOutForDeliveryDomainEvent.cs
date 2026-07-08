using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

// Raised by the Delivery-service-driven transition (Phase 2); modeled now so the state machine is
// complete, no endpoint exposes it this iteration.
public sealed class OrderOutForDeliveryDomainEvent(
    Guid orderId,
    Guid customerId,
    Guid restaurantId) : DomainEvent
{
    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public Guid RestaurantId { get; init; } = restaurantId;
}
