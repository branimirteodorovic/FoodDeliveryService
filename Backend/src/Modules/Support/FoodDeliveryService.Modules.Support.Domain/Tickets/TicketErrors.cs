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

    // ---- Assignment ----------------------------------------------------------------------
    // The aggregate guard behind Claim. The distributed lock in the handler makes two concurrent
    // claims observe it in sequence; this is what actually refuses the second one.
    public static readonly Error AlreadyAssigned = Error.Conflict(
        "Tickets.AlreadyAssigned",
        "The ticket is already assigned to an agent");

    // Re-assigning a ticket to the agent who already holds it changes nothing, so it raises no
    // event — and therefore must not write an audit entry claiming an assignment took place.
    public static readonly Error AlreadyAssignedToAgent = Error.Problem(
        "Tickets.AlreadyAssignedToAgent",
        "The ticket is already assigned to that agent");

    public static readonly Error AgentRequired = Error.Problem(
        "Tickets.AgentRequired",
        "An assignment needs an agent");

    public static readonly Error UnassignReasonRequired = Error.Problem(
        "Tickets.UnassignReasonRequired",
        "Unassigning a ticket requires a reason");

    // Lost the race for the claim lock. A retryable failure rather than a stranding one: the ticket
    // is still sitting in the queue, so the agent's next refresh either shows it taken or offers it
    // again. See SupportLocks.
    public static readonly Error ClaimInProgress = Error.Problem(
        "Tickets.ClaimInProgress",
        "The ticket is being claimed by another agent — try again");

    // Claim is queue-only: a ticket somebody is already working, has resolved or has closed does
    // not go back on the shelf. Separate from NotAssignable because the claimable set is narrower.
    public static Error NotClaimable(TicketStatus status) => Error.Problem(
        "Tickets.NotClaimable",
        $"A ticket with status {status} cannot be claimed");

    public static Error NotAssignable(TicketStatus status) => Error.Problem(
        "Tickets.NotAssignable",
        $"A ticket with status {status} cannot be assigned or unassigned");
}
