using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.IntegrationEvents;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.ApproveRefund;

/// <summary>
/// Publishes the approval so Notifications can tell the customer. That is the whole downstream
/// effect — no payment is triggered here or anywhere else, because this platform has none.
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
    }
}
