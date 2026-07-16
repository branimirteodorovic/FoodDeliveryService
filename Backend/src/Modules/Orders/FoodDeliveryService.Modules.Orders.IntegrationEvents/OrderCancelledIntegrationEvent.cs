using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Orders.IntegrationEvents;

public sealed class OrderCancelledIntegrationEvent : IntegrationEvent
{
    public OrderCancelledIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid orderId,
        Guid customerId,
        Guid restaurantId,
        DateTime cancelledOnUtc)
        : base(id, occurredOnUtc)
    {
        OrderId = orderId;
        CustomerId = customerId;
        RestaurantId = restaurantId;
        CancelledOnUtc = cancelledOnUtc;
    }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public Guid RestaurantId { get; init; }

    public DateTime CancelledOnUtc { get; init; }
}
