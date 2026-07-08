using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Orders.IntegrationEvents;

public sealed class OrderPlacedIntegrationEvent : IntegrationEvent
{
    public OrderPlacedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid orderId,
        Guid customerId,
        Guid restaurantId,
        decimal subtotal,
        DateTime placedOnUtc)
        : base(id, occurredOnUtc)
    {
        OrderId = orderId;
        CustomerId = customerId;
        RestaurantId = restaurantId;
        Subtotal = subtotal;
        PlacedOnUtc = placedOnUtc;
    }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public Guid RestaurantId { get; init; }

    public decimal Subtotal { get; init; }

    public DateTime PlacedOnUtc { get; init; }
}
