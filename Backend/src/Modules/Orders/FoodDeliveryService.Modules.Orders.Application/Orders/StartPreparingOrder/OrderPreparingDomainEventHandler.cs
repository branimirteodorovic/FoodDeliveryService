using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.StartPreparingOrder;

/// <summary>
/// Records the Accepted → Preparing step of the lifecycle. It publishes nothing: no other service
/// acts on "the kitchen started cooking" today, and inventing an integration event for it would put
/// an unconsumed contract on the broker.
/// <para>
/// It exists because the aggregate raised this event and nobody handled it — the same was true of
/// OutForDelivery and Delivered, which is the back half of the lifecycle and precisely the half a
/// "where do orders stall?" panel is about. Counting only in the handlers that happened to exist for
/// integration reasons would have left that half dark. One emission site per transition, all of them
/// on the outbox path.
/// </para>
/// </summary>
internal sealed class OrderPreparingDomainEventHandler : DomainEventHandler<OrderPreparingDomainEvent>
{
    public override Task Handle(
        OrderPreparingDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        OrdersDiagnostics.RecordTransition(domainEvent.PreviousStatus, OrderStatus.Preparing);

        return Task.CompletedTask;
    }
}
