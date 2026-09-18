using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Payments.Application.Diagnostics;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.AuthorizePayment;

/// <summary>
/// Tells the platform the card was not charged. Orders cancels the order on it (§8.3), and
/// Notifications emails the customer about it (§10.4) — which is the reason the bounded reason code
/// travels on the contract rather than staying in this service's logs.
/// </summary>
internal sealed class PaymentAuthorizationFailedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<PaymentAuthorizationFailedDomainEvent>
{
    public override async Task Handle(
        PaymentAuthorizationFailedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await eventBus.PublishAsync(
            new PaymentAuthorizationFailedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.PaymentId,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.Reason,
                domainEvent.FailedOnUtc),
            cancellationToken);

        // The reason is already one of PaymentFailureReason's six constants — the aggregate will not
        // hold anything else — so it reaches the tag without a translation step that could let a
        // Stripe message through. Recorded last, for the reason the sibling handler states.
        PaymentsDiagnostics.RecordFailed(domainEvent.Reason);
    }
}
