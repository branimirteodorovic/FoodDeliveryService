using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

public static class TicketErrors
{
    public static readonly Error SubjectRequired = Error.Problem(
        "Tickets.SubjectRequired",
        "A ticket needs a subject");

    public static readonly Error SubjectTooLong = Error.Problem(
        "Tickets.SubjectTooLong",
        $"A ticket subject cannot be longer than {Ticket.SubjectMaxLength} characters");

    // StartProgress/Resolve/Escalate are agent actions on an agent's ticket — an unassigned ticket
    // has nobody accountable for them. Claim (Milestone C) is what puts an agent on it.
    public static readonly Error NotAssigned = Error.Problem(
        "Tickets.NotAssigned",
        "The ticket has no assigned agent");

    public static readonly Error ResolutionRequired = Error.Problem(
        "Tickets.ResolutionRequired",
        "Resolving a ticket requires a resolution note");

    public static readonly Error EscalationReasonRequired = Error.Problem(
        "Tickets.EscalationReasonRequired",
        "Escalating a ticket requires a reason");

    // The reopen window exists so a resolved ticket eventually settles: past it the customer opens
    // a new ticket, which keeps resolution time an honest measure rather than one an old ticket
    // can reopen months later.
    public static readonly Error ReopenWindowElapsed = Error.Problem(
        "Tickets.ReopenWindowElapsed",
        $"A ticket can only be reopened within {Ticket.ReopenWindowInDays} days of being resolved");

    // Read-guard for a single ticket. NotFound rather than a forbidden-style error on purpose: a
    // customer asking for someone else's ticket must not learn that it exists.
    public static Error NotFound(Guid ticketId) => Error.NotFound(
        "Tickets.NotFound",
        $"The ticket with the identifier {ticketId} was not found");

    public static Error InvalidTransition(TicketStatus from, TicketStatus to) => Error.Problem(
        "Tickets.InvalidTransition",
        $"The ticket cannot move from status {from} to status {to}");
}
