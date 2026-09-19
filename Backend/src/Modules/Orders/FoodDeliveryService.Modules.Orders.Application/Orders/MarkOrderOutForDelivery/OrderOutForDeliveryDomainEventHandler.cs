using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderOutForDelivery;

/// <summary>
/// Records the ReadyForPickup → OutForDelivery step. Publishes nothing — this transition is itself
/// driven by Delivery's <c>OrderPickedUp</c> event, so the service that would care already knows.
/// <para>
/// This and <see cref="MarkOrderDelivered.OrderDeliveredDomainEventHandler"/> are the metrics-only
/// handlers: they exist because the aggregate raised the event and nobody handled it, which is the
/// back half of the lifecycle and precisely the half a "where do orders stall?" panel is about.
/// Counting only in the handlers that happened to exist for integration reasons would have left that
/// half dark. One emission site per transition, all of them on the outbox path.
/// </para>
/// </summary>
internal sealed class OrderOutForDeliveryDomainEventHandler
    : DomainEventHandler<OrderOutForDeliveryDomainEvent>
{
    public override Task Handle(
        OrderOutForDeliveryDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        OrdersDiagnostics.RecordTransition(domainEvent.PreviousStatus, OrderStatus.OutForDelivery);

        return Task.CompletedTask;
    }
}
