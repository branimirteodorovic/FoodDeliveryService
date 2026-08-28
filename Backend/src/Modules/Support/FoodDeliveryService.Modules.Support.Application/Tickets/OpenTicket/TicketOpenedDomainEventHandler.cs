using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Application.Diagnostics;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using FoodDeliveryService.Modules.Support.IntegrationEvents;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.OpenTicket;

// The domain event already carries the full opening snapshot, so nothing is read back here.
// Consumers (the live agent dashboard, the customer notification) are wired in later milestones.
internal sealed class TicketOpenedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<TicketOpenedDomainEvent>
{
    public override async Task Handle(
        TicketOpenedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new SupportTicketOpenedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.TicketId,
                domainEvent.Reference,
                domainEvent.CustomerId,
                domainEvent.OrderId,
                domainEvent.Subject,
                domainEvent.Category.ToString(),
                domainEvent.Priority.ToString(),
                // A freshly opened ticket is Open and unassigned by construction — the aggregate
                // has no other way to be born, so this is a constant rather than a read-back.
                TicketStatus.Open.ToString(),
                domainEvent.Source.ToString(),
                assignedAgentId: null,
                domainEvent.OpenedOnUtc),
            cancellationToken);

        // Recorded LAST, after the publish: IdempotentDomainEventHandler only writes its
        // consumer row once Handle returns, so a handler that throws is re-run whole on the
        // next outbox tick — counting first would inflate the series by every retry.
        SupportDiagnostics.RecordOpened(domainEvent.Category);
    }
}
