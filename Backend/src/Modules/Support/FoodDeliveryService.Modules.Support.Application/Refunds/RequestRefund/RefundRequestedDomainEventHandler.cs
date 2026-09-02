using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.IntegrationEvents;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.RequestRefund;

// The event carries everything the aggregate holds about the request (hard rule #9), so nothing
// downstream needs to ask Support what a refund was for.
internal sealed class RefundRequestedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<RefundRequestedDomainEvent>
{
    public override async Task Handle(
        RefundRequestedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new RefundRequestedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.RefundRequestId,
                domainEvent.TicketId,
                domainEvent.TicketReference,
                domainEvent.OrderId,
                domainEvent.CustomerId,
                domainEvent.Amount,
                domainEvent.Reason,
                domainEvent.RequestedByAgentId,
                domainEvent.RequestedOnUtc),
            cancellationToken);
    }
}
