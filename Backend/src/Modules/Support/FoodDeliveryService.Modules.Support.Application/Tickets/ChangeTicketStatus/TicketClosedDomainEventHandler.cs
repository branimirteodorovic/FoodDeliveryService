using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Application.Diagnostics;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ChangeTicketStatus;

// Measurement only, for the reason given on TicketProgressStartedDomainEventHandler. Closing is the
// terminal edge of the graph, so its count is also what tells a flat "resolved" line apart from a
// queue where nothing is ever finished off.
internal sealed class TicketClosedDomainEventHandler : DomainEventHandler<TicketClosedDomainEvent>
{
    public override Task Handle(
        TicketClosedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        SupportDiagnostics.RecordTransition(domainEvent.PreviousStatus, TicketStatus.Closed);

        return Task.CompletedTask;
    }
}
