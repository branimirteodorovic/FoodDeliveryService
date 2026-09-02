using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Application.Diagnostics;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.IntegrationEvents;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.RejectRefund;

// The customer is told either way. A refund request that is declined in silence is the outcome a
// support process cannot afford: the customer has no way to tell it from one nobody looked at.
internal sealed class RefundRejectedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<RefundRejectedDomainEvent>
{
    public override async Task Handle(
        RefundRejectedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new RefundRejectedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.RefundRequestId,
                domainEvent.TicketId,
                domainEvent.TicketReference,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.Amount,
                domainEvent.RequestedByAgentId,
                domainEvent.DecidedByAdminId,
                domainEvent.DecisionNote,
                domainEvent.DecidedOnUtc),
            cancellationToken);

        // Recorded LAST, after the publish, like every other measurement in this module: the
        // idempotent wrapper only marks the message handled once Handle returns, so counting first
        // would inflate the series by every outbox retry.
        SupportDiagnostics.RecordRefundDecision(RefundStatus.Rejected);
    }
}
