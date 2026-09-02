using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Application.Diagnostics;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using FoodDeliveryService.Modules.Support.IntegrationEvents;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ChangeTicketStatus;

// OpenedOnUtc travels with the resolution so a consumer can compute how long the case took without
// ever querying Support (hard rules #5 and #9).
internal sealed class TicketResolvedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<TicketResolvedDomainEvent>
{
    public override async Task Handle(
        TicketResolvedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new SupportTicketResolvedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.TicketId,
                domainEvent.Reference,
                domainEvent.CustomerId,
                domainEvent.OrderId,
                domainEvent.AgentId,
                domainEvent.Category.ToString(),
                domainEvent.Resolution,
                domainEvent.OpenedOnUtc,
                domainEvent.ResolvedOnUtc),
            cancellationToken);

        // Last, for the same reason as the opened handler — and computed from the event's own
        // two timestamps, so a message dispatched minutes after the fact still reports the
        // duration the customer actually experienced rather than the outbox lag.
        SupportDiagnostics.RecordResolution(
            domainEvent.Category,
            domainEvent.ResolvedOnUtc - domainEvent.OpenedOnUtc);

        // The resolving edge of the transition graph. Recorded here rather than in a handler of its
        // own so the two measurements a resolution produces cannot drift apart across a retry.
        SupportDiagnostics.RecordTransition(domainEvent.PreviousStatus, TicketStatus.Resolved);
    }
}
