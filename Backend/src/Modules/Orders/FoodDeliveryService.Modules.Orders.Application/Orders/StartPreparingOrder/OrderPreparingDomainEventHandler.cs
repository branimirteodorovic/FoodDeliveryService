using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.StartPreparingOrder;

/// <summary>
/// Publishes the Accepted → Preparing step and records it on the transition counter.
/// <para>
/// This handler used to publish nothing, on the grounds that no service acted on "the kitchen
/// started cooking" and an unconsumed contract on the broker is a cost with no reader. That is no
/// longer true: the RealTime service pushes a live status timeline, and Preparing was the one
/// transition missing from it — a customer watching an order saw Accepted and then nothing until
/// ReadyForPickup. RealTime is the sole consumer; Notifications, Support, Delivery and Payments
/// deliberately do not take this event (see the plan, ORDERS_PHASE1_PLAN.md §Milestone D).
/// </para>
/// <para>
/// The domain event already carries the full snapshot (order/customer/restaurant ids), so nothing is
/// read back before publishing. It carries no Preparing timestamp and does not need one — the
/// consumer uses OccurredOnUtc, as it already does for OrderReadyForPickup.
/// </para>
/// </summary>
internal sealed class OrderPreparingDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<OrderPreparingDomainEvent>
{
    public override async Task Handle(
        OrderPreparingDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new OrderPreparingIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.RestaurantId),
            cancellationToken);

        // Last, so an outbox retry of a failed handler doesn't count the transition twice — see
        // OrderPlacedDomainEventHandler.
        OrdersDiagnostics.RecordTransition(domainEvent.PreviousStatus, OrderStatus.Preparing);
    }
}
