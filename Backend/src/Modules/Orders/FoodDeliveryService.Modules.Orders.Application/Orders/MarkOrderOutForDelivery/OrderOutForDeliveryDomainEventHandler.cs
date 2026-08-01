using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderOutForDelivery;

/// <summary>
/// Records the ReadyForPickup → OutForDelivery step. Publishes nothing — this transition is itself
/// driven by Delivery's <c>OrderPickedUp</c> event, so the service that would care already knows.
/// See <see cref="StartPreparingOrder.OrderPreparingDomainEventHandler"/> for why the metrics-only
/// handlers exist.
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
