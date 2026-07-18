using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.OfferDelivery;

internal sealed class DeliveryUnassignedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<DeliveryUnassignedDomainEvent>
{
    public override async Task Handle(
        DeliveryUnassignedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new DeliveryUnassignedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.DeliveryId,
                domainEvent.OrderId),
            cancellationToken);
    }
}
