using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.MarkDeliveryDelivered;

// Publishes OrderDelivered via the outbox so Orders can advance the order to Delivered — the last
// transition, driven entirely over the bus with no HTTP call between the two services.
internal sealed class DeliveryDeliveredDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<DeliveryDeliveredDomainEvent>
{
    public override async Task Handle(
        DeliveryDeliveredDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new OrderDeliveredIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.OrderId,
                domainEvent.DeliveryId,
                domainEvent.DriverId,
                domainEvent.OccurredOnUtc),
            cancellationToken);
    }
}
