using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.ReleasePayment;

/// <summary>
/// Tells the platform the hold is gone and nothing was charged — Feature 3.8 Milestone G. Orders is
/// the only consumer today; it is published rather than kept local because "was this customer ever
/// charged for the order they cancelled?" is a question support and finance ask, and the answer
/// should not require reading this service's database.
/// </summary>
internal sealed class PaymentReleasedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<PaymentReleasedDomainEvent>
{
    public override async Task Handle(
        PaymentReleasedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await eventBus.PublishAsync(
            new PaymentReleasedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.PaymentId,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.Amount,
                domainEvent.Currency,
                domainEvent.ReleasedOnUtc),
            cancellationToken);
    }
}
