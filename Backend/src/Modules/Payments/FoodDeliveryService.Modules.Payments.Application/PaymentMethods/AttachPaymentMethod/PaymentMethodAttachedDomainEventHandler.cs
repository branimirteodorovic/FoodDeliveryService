using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.AttachPaymentMethod;

/// <summary>
/// Announces that this customer can now be charged by card. Orders is the consumer — it keeps a
/// one-flag replica so a card order can be refused at placement without asking this service
/// anything (§6.3, hard rule #9).
/// <para>
/// The <c>pm_…</c> identifier is not on the event and must not be added to it. No other service can
/// do anything with a Stripe payment method except hold it, and a provider credential replicated
/// into four databases is four places it has to be scrubbed from.
/// </para>
/// </summary>
internal sealed class PaymentMethodAttachedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<PaymentMethodAttachedDomainEvent>
{
    public override async Task Handle(
        PaymentMethodAttachedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new PaymentMethodAttachedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.CustomerId,
                domainEvent.PaymentMethodId,
                domainEvent.Brand,
                domainEvent.Last4,
                domainEvent.ExpiryMonth,
                domainEvent.ExpiryYear,
                domainEvent.AttachedOnUtc),
            cancellationToken);
    }
}
