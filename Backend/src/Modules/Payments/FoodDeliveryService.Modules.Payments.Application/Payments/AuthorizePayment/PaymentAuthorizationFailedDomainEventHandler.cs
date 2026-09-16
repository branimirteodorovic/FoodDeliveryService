using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
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
    }
}
