using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Application.Diagnostics;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.IntegrationEvents;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.ApproveRefund;

/// <summary>
/// Publishes the approval, which is what actually triggers the refund.
/// <para>
/// It used to be true that this had no downstream effect beyond the customer's email. Feature 3.8
/// added the Payments service, which consumes this event, refunds the captured card payment and
/// answers with <c>RefundSettled</c> or <c>RefundFailed</c> — so this publish is now the moment real
/// money starts moving, and the segregation of duties that got the aggregate here is what
/// authorizes it.
/// </para>
/// </summary>
internal sealed class RefundApprovedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<RefundApprovedDomainEvent>
{
    public override async Task Handle(
        RefundApprovedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new RefundApprovedIntegrationEvent(
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
        SupportDiagnostics.RecordRefundDecision(RefundStatus.Approved);
    }
}
