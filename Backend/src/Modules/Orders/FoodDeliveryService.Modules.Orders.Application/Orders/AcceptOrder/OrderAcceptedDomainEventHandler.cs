using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.AcceptOrder;

// The transition domain event carries the full snapshot (order/customer/restaurant ids + timestamp),
// so no read-back query is needed before publishing.
internal sealed class OrderAcceptedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<OrderAcceptedDomainEvent>
{
    public override async Task Handle(
        OrderAcceptedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new OrderAcceptedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.RestaurantId,
                domainEvent.AcceptedOnUtc),
            cancellationToken);
    }
}
