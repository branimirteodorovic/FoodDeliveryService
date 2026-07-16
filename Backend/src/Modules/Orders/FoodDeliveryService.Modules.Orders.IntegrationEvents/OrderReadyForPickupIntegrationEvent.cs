using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Orders.IntegrationEvents;

// Minimal snapshot for Phase 1 (ids only). The Delivery service (Phase 2, Feature 2.1 Milestone D)
// extends this contract with the restaurant + delivery-address coordinates the assignment routine
// needs — that geo enrichment is deliberately out of scope here.
public sealed class OrderReadyForPickupIntegrationEvent : IntegrationEvent
{
    public OrderReadyForPickupIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid orderId,
        Guid customerId,
        Guid restaurantId)
        : base(id, occurredOnUtc)
    {
        OrderId = orderId;
        CustomerId = customerId;
        RestaurantId = restaurantId;
    }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public Guid RestaurantId { get; init; }
}
