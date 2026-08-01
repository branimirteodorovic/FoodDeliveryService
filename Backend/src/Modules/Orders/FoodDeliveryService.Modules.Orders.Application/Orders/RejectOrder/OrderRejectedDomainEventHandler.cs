using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.RejectOrder;

internal sealed class OrderRejectedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<OrderRejectedDomainEvent>
{
    public override async Task Handle(
        OrderRejectedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new OrderRejectedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.RestaurantId,
                domainEvent.Reason,
                domainEvent.RejectedOnUtc),
            cancellationToken);

        // Last, so an outbox retry of a failed handler doesn't count the transition twice — see
        // OrderPlacedDomainEventHandler.
        OrdersDiagnostics.RecordTransition(domainEvent.PreviousStatus, OrderStatus.Rejected);
    }
}
