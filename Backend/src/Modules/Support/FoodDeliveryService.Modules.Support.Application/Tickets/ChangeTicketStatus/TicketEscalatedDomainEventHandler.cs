using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Application.Diagnostics;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ChangeTicketStatus;

// Measurement only, for the reason given on TicketProgressStartedDomainEventHandler. The escalation
// reason stays out of the tags: it is free text an agent typed, and one series per phrasing is how
// a metrics backend gets taken down by a support queue.
internal sealed class TicketEscalatedDomainEventHandler : DomainEventHandler<TicketEscalatedDomainEvent>
{
    public override Task Handle(
        TicketEscalatedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        SupportDiagnostics.RecordTransition(domainEvent.PreviousStatus, TicketStatus.Escalated);

        return Task.CompletedTask;
    }
}
