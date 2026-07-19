using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.MarkDeliveryPickedUp;

// Publishes OrderPickedUp via the outbox so Orders can advance the order to OutForDelivery. The
// event's OccurredOnUtc is the pickup moment (the aggregate raised it as the status flipped).
internal sealed class DeliveryPickedUpDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<DeliveryPickedUpDomainEvent>
{
    public override async Task Handle(
        DeliveryPickedUpDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new OrderPickedUpIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.OrderId,
                domainEvent.DeliveryId,
                domainEvent.DriverId,
                domainEvent.OccurredOnUtc),
            cancellationToken);
    }
}
