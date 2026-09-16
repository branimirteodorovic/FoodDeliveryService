using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.FailOrderPayment;

/// <summary>
/// A card order ended because the card was refused — Feature 3.8 Milestone F, §8.3.
/// <para>
/// <b>It publishes nothing, and that is a decision rather than an omission.</b> The event this
/// cancellation would announce has already been announced, better: Payments publishes
/// <c>PaymentAuthorizationFailedIntegrationEvent</c> carrying the bounded reason, and that is what
/// Notifications consumes to tell the customer their card was declined (§10.4). An
/// <c>OrderPaymentFailed</c> integration event beside it would be a second message about one fact,
/// with less information on it.
/// </para>
/// <para>
/// The consequence worth knowing: Delivery and RealTime learn about ordinary cancellations from
/// <c>OrderCancelledIntegrationEvent</c> and do not learn about this one. Delivery is unaffected — no
/// delivery exists for an order that never left <c>Pending</c> — but a customer watching the live
/// tracker sees the order stop rather than turn red, until whatever consumes the payment failure
/// also pushes a frame. Recorded here so the next milestone that touches this path knows it is a
/// known gap and not an oversight.
/// </para>
/// </summary>
internal sealed class OrderPaymentFailedDomainEventHandler : DomainEventHandler<OrderPaymentFailedDomainEvent>
{
    public override Task Handle(
        OrderPaymentFailedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // Recorded last, as every counter in this module is — the idempotent decorator only writes
        // its consumer row once Handle returns, so a handler that threw earlier would be re-run
        // whole and would count twice. Here it is also the only thing the handler does.
        OrdersDiagnostics.RecordTransition(domainEvent.PreviousStatus, OrderStatus.Cancelled);

        return Task.CompletedTask;
    }
}
