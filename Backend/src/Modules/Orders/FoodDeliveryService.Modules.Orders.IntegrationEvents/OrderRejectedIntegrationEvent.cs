using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Orders.IntegrationEvents;

public sealed class OrderRejectedIntegrationEvent : IntegrationEvent
{
    public OrderRejectedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid orderId,
        Guid customerId,
        Guid restaurantId,
        string reason,
        DateTime rejectedOnUtc)
        : base(id, occurredOnUtc)
    {
        OrderId = orderId;
        CustomerId = customerId;
        RestaurantId = restaurantId;
        Reason = reason;
        RejectedOnUtc = rejectedOnUtc;
    }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public Guid RestaurantId { get; init; }

    public string Reason { get; init; }

    public DateTime RejectedOnUtc { get; init; }
}
