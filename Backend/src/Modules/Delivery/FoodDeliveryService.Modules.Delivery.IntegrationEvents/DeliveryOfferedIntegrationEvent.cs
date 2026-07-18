using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Delivery.IntegrationEvents;

// Consumed by Feature 2.2's real-time push and support tooling. The re-offer loop itself is
// in-service — the reject handler and the expiry job call the assignment routine directly, not via
// this event.
public sealed class DeliveryOfferedIntegrationEvent : IntegrationEvent
{
    public DeliveryOfferedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid deliveryId,
        Guid orderId,
        Guid driverId,
        DateTime offerExpiresOnUtc)
        : base(id, occurredOnUtc)
    {
        DeliveryId = deliveryId;
        OrderId = orderId;
        DriverId = driverId;
        OfferExpiresOnUtc = offerExpiresOnUtc;
    }

    public Guid DeliveryId { get; init; }

    public Guid OrderId { get; init; }

    public Guid DriverId { get; init; }

    public DateTime OfferExpiresOnUtc { get; init; }
}
