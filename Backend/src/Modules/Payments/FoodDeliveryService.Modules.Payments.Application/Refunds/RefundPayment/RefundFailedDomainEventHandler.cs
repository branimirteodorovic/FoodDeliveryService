using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Payments.Domain.Refunds;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;

namespace FoodDeliveryService.Modules.Payments.Application.Refunds.RefundPayment;

/// <summary>
/// Carries a refusal back to the people who were told the refund was agreed — Feature 3.8
/// Milestone H. Support is the only consumer; see the integration event for why Notifications is
/// deliberately not one.
/// </summary>
internal sealed class RefundFailedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<RefundFailedDomainEvent>
{
    public override async Task Handle(
        RefundFailedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await eventBus.PublishAsync(
            new RefundFailedIntegrationEvent(
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
                domainEvent.Reason,
                domainEvent.FailedOnUtc),
            cancellationToken);
    }
}
