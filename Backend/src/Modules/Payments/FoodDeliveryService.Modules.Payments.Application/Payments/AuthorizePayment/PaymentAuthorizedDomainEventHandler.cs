using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Payments.Application.Diagnostics;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.AuthorizePayment;

/// <summary>
/// Tells the platform the order's money is held. The domain event already carries the full snapshot,
/// so nothing is read back — this handler runs on the outbox, after the transaction that raised it.
/// </summary>
internal sealed class PaymentAuthorizedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<PaymentAuthorizedDomainEvent>
{
    public override async Task Handle(
        PaymentAuthorizedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await eventBus.PublishAsync(
            new PaymentAuthorizedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.PaymentId,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.Amount,
                domainEvent.Currency,
                domainEvent.AuthorizedOnUtc),
            cancellationToken);

        // Last, and after the publish: this handler is dispatched at least once and is not retried
        // piecemeal, so a throw above means the whole thing runs again — counting first would inflate
        // the series by exactly the failures an operator is trying to see.
        PaymentsDiagnostics.RecordAuthorized();
    }
}
