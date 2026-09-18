using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Payments.Application.Diagnostics;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.CapturePayment;

/// <summary>
/// Tells the platform the money actually moved — Feature 3.8 Milestone G. Orders projects it onto
/// the order's payment dimension; §10's refunds are measured against it.
/// </summary>
internal sealed class PaymentCapturedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<PaymentCapturedDomainEvent>
{
    public override async Task Handle(
        PaymentCapturedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await eventBus.PublishAsync(
            new PaymentCapturedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.PaymentId,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.Amount,
                domainEvent.Currency,
                domainEvent.CapturedOnUtc),
            cancellationToken);

        PaymentsDiagnostics.RecordCaptured();
    }
}
