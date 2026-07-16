using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Orders.IntegrationEvents;

public sealed class OrderAcceptedIntegrationEvent : IntegrationEvent
{
    public OrderAcceptedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid orderId,
        Guid customerId,
        Guid restaurantId,
        DateTime acceptedOnUtc)
        : base(id, occurredOnUtc)
    {
        OrderId = orderId;
        CustomerId = customerId;
        RestaurantId = restaurantId;
        AcceptedOnUtc = acceptedOnUtc;
    }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public Guid RestaurantId { get; init; }

    public DateTime AcceptedOnUtc { get; init; }
}
