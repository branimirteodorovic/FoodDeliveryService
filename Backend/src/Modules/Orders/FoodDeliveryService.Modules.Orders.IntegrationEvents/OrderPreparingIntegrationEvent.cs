using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Orders.IntegrationEvents;

// The kitchen has started cooking. Consumed by the RealTime service, which turns it into the
// Preparing frame on the customer's live order timeline and on the restaurant/support dashboards.
//
// There is no PreparingOnUtc: unlike Accept, the aggregate records no status-specific timestamp for
// this transition, and this event is not a reason to add one. Consumers take OccurredOnUtc as the
// transition time, exactly as they already do for OrderReadyForPickup.
public sealed class OrderPreparingIntegrationEvent : IntegrationEvent
{
    public OrderPreparingIntegrationEvent(
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
