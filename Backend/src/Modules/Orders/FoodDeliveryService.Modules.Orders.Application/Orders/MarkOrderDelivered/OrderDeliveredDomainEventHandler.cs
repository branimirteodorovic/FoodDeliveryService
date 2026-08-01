using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderDelivered;

/// <summary>
/// Records the OutForDelivery → Delivered step — the terminal success of the whole funnel, and the
/// denominator for any completion-rate panel. Publishes nothing, for the same reason as the other
/// metrics-only handlers (see
/// <see cref="StartPreparingOrder.OrderPreparingDomainEventHandler"/>): Delivery drove this
/// transition, and no other service reacts to it today.
/// </summary>
internal sealed class OrderDeliveredDomainEventHandler : DomainEventHandler<OrderDeliveredDomainEvent>
{
    public override Task Handle(
        OrderDeliveredDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        OrdersDiagnostics.RecordTransition(domainEvent.PreviousStatus, OrderStatus.Delivered);

        return Task.CompletedTask;
    }
}
