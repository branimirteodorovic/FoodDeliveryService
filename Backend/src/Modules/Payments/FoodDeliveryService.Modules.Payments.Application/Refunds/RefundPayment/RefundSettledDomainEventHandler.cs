using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Payments.Application.Diagnostics;
using FoodDeliveryService.Modules.Payments.Domain.Refunds;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;

namespace FoodDeliveryService.Modules.Payments.Application.Refunds.RefundPayment;

/// <summary>
/// Tells Support its refund actually happened, and Notifications to say so to the customer —
/// Feature 3.8 Milestone H.
/// </summary>
internal sealed class RefundSettledDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<RefundSettledDomainEvent>
{
    public override async Task Handle(
        RefundSettledDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await eventBus.PublishAsync(
            new RefundSettledIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.RefundId,
                domainEvent.RefundRequestId,
                domainEvent.TicketId,
                domainEvent.TicketReference,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.Amount,
                domainEvent.Currency,
                domainEvent.SettledOnUtc),
            cancellationToken);

        PaymentsDiagnostics.RecordRefundSettled();
    }
}
