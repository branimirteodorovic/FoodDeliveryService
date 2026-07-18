using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.OfferDelivery;

internal sealed class DeliveryOfferedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<DeliveryOfferedDomainEvent>
{
    public override async Task Handle(
        DeliveryOfferedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new DeliveryOfferedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.DeliveryId,
                domainEvent.OrderId,
                domainEvent.DriverId,
                domainEvent.OfferExpiresOnUtc),
            cancellationToken);
    }
}
