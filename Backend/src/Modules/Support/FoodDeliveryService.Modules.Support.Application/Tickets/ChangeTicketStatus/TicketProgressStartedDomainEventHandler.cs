using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Support.Application.Diagnostics;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ChangeTicketStatus;

/// <summary>
/// Measurement only — no integration event. Work starting on a ticket is a fact about the support
/// queue and nothing outside Support reacts to it, so publishing one would put a message on the
/// broker that every consumer ignores.
/// <para>
/// The handler exists at all because the transition counter has to be fed from the outbox path
/// like every other measurement in this module: recording it in the command handler instead would
/// make the metric the one thing in Support that a rolled-back <c>SaveChangesAsync</c> could still
/// have counted.
/// </para>
/// </summary>
internal sealed class TicketProgressStartedDomainEventHandler
    : DomainEventHandler<TicketProgressStartedDomainEvent>
{
    public override Task Handle(
        TicketProgressStartedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        SupportDiagnostics.RecordTransition(domainEvent.PreviousStatus, TicketStatus.InProgress);

        return Task.CompletedTask;
    }
}
