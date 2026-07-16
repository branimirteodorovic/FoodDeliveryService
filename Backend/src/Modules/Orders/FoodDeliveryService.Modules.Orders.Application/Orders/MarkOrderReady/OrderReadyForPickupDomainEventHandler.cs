using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderReady;

// This is the event the Delivery service (Phase 2) consumes to start driver assignment.
internal sealed class OrderReadyForPickupDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<OrderReadyForPickupDomainEvent>
{
    public override async Task Handle(
        OrderReadyForPickupDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new OrderReadyForPickupIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.RestaurantId),
            cancellationToken);
    }
}
