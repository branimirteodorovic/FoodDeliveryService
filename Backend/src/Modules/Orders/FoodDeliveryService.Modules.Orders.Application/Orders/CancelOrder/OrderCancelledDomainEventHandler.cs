using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
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
    }
}
