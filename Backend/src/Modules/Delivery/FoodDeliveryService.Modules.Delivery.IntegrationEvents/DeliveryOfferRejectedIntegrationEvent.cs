using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Delivery.IntegrationEvents;

public sealed class DeliveryOfferRejectedIntegrationEvent : IntegrationEvent
{
    public DeliveryOfferRejectedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid deliveryId,
        Guid orderId,
        Guid driverId)
        : base(id, occurredOnUtc)
    {
        DeliveryId = deliveryId;
        OrderId = orderId;
        DriverId = driverId;
    }

    public Guid DeliveryId { get; init; }

    public Guid OrderId { get; init; }

    public Guid DriverId { get; init; }
}
