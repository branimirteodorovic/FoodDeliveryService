using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Application.Diagnostics;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ChangeTicketStatus;

/// <summary>
/// Measurement only. Reopens are the one transition worth watching on its own: the ratio of
/// Resolved→InProgress to Resolved is how often a resolution did not hold, which no
/// time-to-resolution number can show — a ticket resolved fast and reopened twice reads as the
/// fastest case on the dashboard.
/// <para>
/// It covers both roads to a reopen, the agent-driven one and the customer replying on a resolved
/// ticket, because <c>Ticket.PostMessage</c> deliberately raises the same event rather than a
/// second one (see its comment).
/// </para>
/// </summary>
internal sealed class TicketReopenedDomainEventHandler : DomainEventHandler<TicketReopenedDomainEvent>
{
    public override Task Handle(
        TicketReopenedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        SupportDiagnostics.RecordTransition(domainEvent.PreviousStatus, TicketStatus.InProgress);

        return Task.CompletedTask;
    }
}
