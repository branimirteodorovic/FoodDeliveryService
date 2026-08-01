using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.PlaceOrder;

// The domain event already carries the full placement snapshot, so no read-back query is needed.
// Consumers are wired later.
internal sealed class OrderPlacedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<OrderPlacedDomainEvent>
{
    public override async Task Handle(
        OrderPlacedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new OrderPlacedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.RestaurantId,
                domainEvent.Subtotal,
                domainEvent.PlacedOnUtc),
            cancellationToken);

        // Recorded LAST, after the handler's real work: IdempotentDomainEventHandler only writes its
        // consumer row once Handle returns, so a handler that throws is re-run whole on the next
        // outbox tick — counting first would inflate the series by every retry.
        //
        // Placement both starts the funnel (orders.placed) and is its first transition, so the
        // state_transition graph has its entry edge rather than starting mid-lifecycle.
        OrdersDiagnostics.RecordPlaced();
        OrdersDiagnostics.RecordTransition(from: null, OrderStatus.Pending);
    }
}
