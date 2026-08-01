using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.CancelOrder;

internal sealed class OrderCancelledDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<OrderCancelledDomainEvent>
{
    public override async Task Handle(
        OrderCancelledDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new OrderCancelledIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.RestaurantId,
                domainEvent.CancelledOnUtc),
            cancellationToken);

        // Last, so an outbox retry doesn't count twice (see OrderPlacedDomainEventHandler). The
        // `from` tag is what makes this one interesting: a cancellation out of Accepted has already
        // cost the restaurant something, one out of Pending has not.
        OrdersDiagnostics.RecordTransition(domainEvent.PreviousStatus, OrderStatus.Cancelled);
    }
}
