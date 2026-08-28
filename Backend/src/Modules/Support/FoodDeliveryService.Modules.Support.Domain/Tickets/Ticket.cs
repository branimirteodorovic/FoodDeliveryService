using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// Aggregate root for one support case. The state machine below IS the domain logic of this
/// feature — every legality rule lives here and nowhere else, which is why the status endpoint is
/// one endpoint dispatching to these methods rather than five verb endpoints re-deriving the table.
///
/// Every transition returns a <see cref="Result"/> and never throws: an illegal move is an ordinary
/// business failure the endpoint turns into a 400, not an exception. A no-op (resolving an already
/// resolved ticket) is a failure too, and deliberately raises no domain event — a redundant call
/// must not put an integration event on the bus.
///
/// Note for this milestone: nothing assigns an agent yet (Claim/AssignTo arrive with the
/// assignment milestone), so the transitions that require an assignee are reachable only once that
/// ships. Escalate is the one agent transition that works today, by design — it is the only one
/// whose meaning does not depend on somebody owning the ticket.
/// </summary>
public sealed class Ticket : Entity
{
    public const int SubjectMaxLength = 200;

    /// <summary>
    /// How long after resolution a ticket can still be reopened. Past it the customer opens a new
    /// ticket instead, so a months-old case cannot silently re-enter the queue — or distort the
    /// average-resolution-time numerator.
    /// </summary>
    public const int ReopenWindowInDays = 7;

    private Ticket()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The human-quotable identifier (SUP-00001234) an agent and a customer can say out loud.
    /// Allocated from a Postgres sequence by the repository — never MAX()+1, which is a race the
    /// moment this service runs two replicas.
    /// </summary>
    public string Reference { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>Nullable: not every ticket is about an order.</summary>
    public Guid? OrderId { get; private set; }

    public string Subject { get; private set; }

    public TicketCategory Category { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    public TicketSource Source { get; private set; }

    /// <summary>
    /// Reserved for the AI assistant (Feature 3.1/3.2): the chatbot conversation that preceded a
    /// human escalation. That feature has no producer in this tree, so the column exists and stays
    /// null — nothing in Support writes it. See <see cref="TicketSource.Chatbot"/>.
    /// </summary>
    public string? EscalationTranscript { get; private set; }

    public Guid? AssignedAgentId { get; private set; }

    public DateTime OpenedOnUtc { get; private set; }

    /// <summary>
    /// Stamped by the first customer-visible agent message (the ticket message thread milestone).
    /// Nothing here sets it — a status change is not a response to the customer.
    /// </summary>
    public DateTime? FirstRespondedOnUtc { get; private set; }

    /// <summary>The numerator of average resolution time. Cleared by <see cref="Reopen"/>.</summary>
    public DateTime? ResolvedOnUtc { get; private set; }

    public DateTime? ClosedOnUtc { get; private set; }

    public static Result<Ticket> Create(
        string reference,
        Guid customerId,
        Guid? orderId,
        string subject,
        TicketCategory category,
        TicketSource source,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Result.Failure<Ticket>(TicketErrors.SubjectRequired);
        }

        if (subject.Length > SubjectMaxLength)
        {
            return Result.Failure<Ticket>(TicketErrors.SubjectTooLong);
        }

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Reference = reference,
            CustomerId = customerId,
            OrderId = orderId,
            Subject = subject,
            Category = category,

            // A customer reporting that their food never arrived is the one category where waiting
            // in a first-in-first-out queue is itself the failure being reported.
            Priority = category == TicketCategory.OrderNotReceived ? TicketPriority.High : TicketPriority.Normal,
            Status = TicketStatus.Open,
            Source = source,
            OpenedOnUtc = utcNow
        };

        ticket.Raise(new TicketOpenedDomainEvent(
            ticket.Id,
            ticket.Reference,
            ticket.CustomerId,
            ticket.OrderId,
            ticket.Subject,
            ticket.Category,
            ticket.Priority,
            ticket.Source,
            ticket.OpenedOnUtc));

        return ticket;
    }

    /// <summary>An assigned agent starts (or resumes, from Escalated) work on the ticket.</summary>
    public Result StartProgress(Guid agentId)
    {
        if (Status is not (TicketStatus.Open or TicketStatus.Escalated))
        {
            return Result.Failure(TicketErrors.InvalidTransition(Status, TicketStatus.InProgress));
        }

        if (AssignedAgentId is null)
        {
            return Result.Failure(TicketErrors.NotAssigned);
        }

        Status = TicketStatus.InProgress;

        Raise(new TicketProgressStartedDomainEvent(Id, agentId));

        return Result.Success();
    }

    public Result Resolve(Guid agentId, string resolution, DateTime utcNow)
    {
        if (Status is not (TicketStatus.InProgress or TicketStatus.Escalated))
        {
            return Result.Failure(TicketErrors.InvalidTransition(Status, TicketStatus.Resolved));
        }

        if (AssignedAgentId is null)
        {
            return Result.Failure(TicketErrors.NotAssigned);
        }

        if (string.IsNullOrWhiteSpace(resolution))
        {
            return Result.Failure(TicketErrors.ResolutionRequired);
        }

        Status = TicketStatus.Resolved;
        ResolvedOnUtc = utcNow;

        Raise(new TicketResolvedDomainEvent(
            Id,
            Reference,
            CustomerId,
            OrderId,
            agentId,
            Category,
            resolution,
            OpenedOnUtc,
            utcNow));

        return Result.Success();
    }

    /// <summary>
    /// Hands the ticket up to a supervisor. The current assignee is kept on purpose — escalation
    /// asks for help, it does not put the ticket back in the queue.
    /// </summary>
    public Result Escalate(Guid agentId, string reason)
    {
        if (Status is not (TicketStatus.Open or TicketStatus.InProgress))
        {
            return Result.Failure(TicketErrors.InvalidTransition(Status, TicketStatus.Escalated));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(TicketErrors.EscalationReasonRequired);
        }

        Status = TicketStatus.Escalated;

        Raise(new TicketEscalatedDomainEvent(Id, agentId, reason));

        return Result.Success();
    }

    /// <summary>
    /// The resolution did not hold. Returns to InProgress rather than Open — the ticket keeps the
    /// agent who already has the context — and clears ResolvedOnUtc so the analytics numerator does
    /// not keep counting a resolution that was undone.
    /// </summary>
    public Result Reopen(Guid actorId, DateTime utcNow)
    {
        if (Status != TicketStatus.Resolved)
        {
            return Result.Failure(TicketErrors.InvalidTransition(Status, TicketStatus.InProgress));
        }

        if (utcNow > ResolvedOnUtc!.Value.AddDays(ReopenWindowInDays))
        {
            return Result.Failure(TicketErrors.ReopenWindowElapsed);
        }

        Status = TicketStatus.InProgress;
        ResolvedOnUtc = null;

        Raise(new TicketReopenedDomainEvent(Id, actorId));

        return Result.Success();
    }

    /// <summary>Terminal. Nothing transitions out of Closed, reopen included.</summary>
    public Result Close(Guid actorId, DateTime utcNow)
    {
        if (Status != TicketStatus.Resolved)
        {
            return Result.Failure(TicketErrors.InvalidTransition(Status, TicketStatus.Closed));
        }

        Status = TicketStatus.Closed;
        ClosedOnUtc = utcNow;

        Raise(new TicketClosedDomainEvent(Id, actorId, utcNow));

        return Result.Success();
    }

    /// <summary>
    /// The single write path for the assignee field. Internal on purpose: no command handler can
    /// reach it, so this milestone ships no way for an agent to take a ticket — that is the
    /// assignment milestone, where the public Claim/AssignTo/Unassign methods wrap this with their
    /// own guards and the distributed lock that keeps two agents from claiming the same ticket.
    /// Adding it now keeps that from becoming a second, unguarded write path later, and lets the
    /// unit tests reach the assignment-dependent half of the state machine.
    /// </summary>
    internal void SetAssignedAgent(Guid? agentId) => AssignedAgentId = agentId;
}
