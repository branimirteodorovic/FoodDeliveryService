using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.RejectDeliveryOffer;

internal sealed class DeliveryOfferRejectedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<DeliveryOfferRejectedDomainEvent>
{
    public override async Task Handle(
        DeliveryOfferRejectedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new DeliveryOfferRejectedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.DeliveryId,
                domainEvent.OrderId,
                domainEvent.DriverId),
            cancellationToken);
    }
}
