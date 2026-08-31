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
/// Assignment (Claim/AssignTo/Unassign) is part of the same state, but deliberately not part of
/// the status machine: claiming a ticket says who owns it, not that work has started. All three go
/// through one internal setter, so there is exactly one place the assignee is ever written.
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

    /// <summary>
    /// The conversation, newest-last. Loaded only when a message is being posted — every read of a
    /// thread goes through Dapper, where the customer-visible filter lives in the SQL.
    /// </summary>
    private readonly List<TicketMessage> _messages = [];

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
    /// Stamped by the first customer-visible agent message, in <see cref="PostMessage"/>. No status
    /// transition sets it — a status change is not a response to the customer.
    /// </summary>
    public DateTime? FirstRespondedOnUtc { get; private set; }

    /// <summary>The numerator of average resolution time. Cleared by <see cref="Reopen"/>.</summary>
    public DateTime? ResolvedOnUtc { get; private set; }

    public DateTime? ClosedOnUtc { get; private set; }

    /// <summary>A defensive copy — the thread is append-only and grows only through PostMessage.</summary>
    public IReadOnlyCollection<TicketMessage> Messages => _messages.ToList();

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
    /// An agent takes an unassigned ticket out of the queue for themselves. Status is untouched —
    /// claiming says who owns the ticket, StartProgress says work has begun, and conflating them
    /// would make "assigned but not yet started" unrepresentable.
    /// <para>
    /// The unassigned check here is the rule of record. The distributed lock the handler takes
    /// around it only makes two concurrent claims observe this guard in sequence; it never replaces
    /// it, and a future write path that skips the lock still cannot double-assign.
    /// </para>
    /// </summary>
    public Result Claim(Guid agentId)
    {
        if (Status is not (TicketStatus.Open or TicketStatus.Escalated))
        {
            return Result.Failure(TicketErrors.NotClaimable(Status));
        }

        if (AssignedAgentId is not null)
        {
            return Result.Failure(TicketErrors.AlreadyAssigned);
        }

        SetAssignedAgent(agentId);

        Raise(new TicketClaimedDomainEvent(Id, agentId));

        return Result.Success();
    }

    /// <summary>
    /// Assignment by somebody other than the new owner — the administrator override, which unlike
    /// <see cref="Claim"/> may take a ticket off the agent who currently holds it. Whether the
    /// caller is allowed to name an agent other than themselves is an authorization question the
    /// application layer answers; the aggregate only enforces what makes a ticket assignable.
    /// </summary>
    public Result AssignTo(Guid agentId, Guid actorId)
    {
        if (agentId == Guid.Empty)
        {
            return Result.Failure(TicketErrors.AgentRequired);
        }

        if (Status is not (TicketStatus.Open or TicketStatus.InProgress or TicketStatus.Escalated))
        {
            return Result.Failure(TicketErrors.NotAssignable(Status));
        }

        // A no-op must not raise an event: re-sending the same assignment would otherwise put a
        // second "assigned" entry in the audit log for something that did not happen.
        if (AssignedAgentId == agentId)
        {
            return Result.Failure(TicketErrors.AlreadyAssignedToAgent);
        }

        Guid? previousAgentId = AssignedAgentId;

        SetAssignedAgent(agentId);

        Raise(new TicketAssignedDomainEvent(Id, agentId, actorId, previousAgentId));

        return Result.Success();
    }

    /// <summary>
    /// Returns the ticket to the queue. The reason is required because this is the one assignment
    /// action whose "why" cannot be inferred from the outcome — an unexplained hand-back is exactly
    /// what the audit log exists to make impossible.
    /// </summary>
    public Result Unassign(Guid actorId, string reason)
    {
        if (Status is not (TicketStatus.Open or TicketStatus.InProgress or TicketStatus.Escalated))
        {
            return Result.Failure(TicketErrors.NotAssignable(Status));
        }

        if (AssignedAgentId is null)
        {
            return Result.Failure(TicketErrors.NotAssigned);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(TicketErrors.UnassignReasonRequired);
        }

        Guid previousAgentId = AssignedAgentId.Value;

        SetAssignedAgent(null);

        Raise(new TicketUnassignedDomainEvent(Id, actorId, previousAgentId, reason));

        return Result.Success();
    }

    /// <summary>
    /// Adds a message to the thread. Not a status transition, but it can move the ticket: a message
    /// on a resolved ticket is how a conversation resumes, so the ticket returns to InProgress and
    /// its resolution timestamp is cleared — the alternative is a ticket that is being actively
    /// discussed while still counting as resolved in the analytics numerator.
    /// <para>
    /// The customer-may-only-write-customer-visible rule is here rather than only at the endpoint on
    /// purpose. An internal note authored by a customer is a data-integrity bug, not an
    /// authorization one: it would sit in the table looking like agent-to-agent commentary forever,
    /// and no amount of endpoint gating added later would find it.
    /// </para>
    /// </summary>
    /// <param name="authorId">The authenticated caller. Never a request-body field.</param>
    /// <param name="kind">
    /// Decided by the application layer from the caller's permissions, which is what makes the rule
    /// above enforceable here — the aggregate has no notion of who is calling.
    /// </param>
    public Result<TicketMessage> PostMessage(
        Guid authorId,
        TicketAuthorKind kind,
        string body,
        TicketMessageVisibility visibility,
        DateTime utcNow)
    {
        // Terminal means terminal. A closed ticket that could still take messages would be a thread
        // nobody is accountable for: it has no assignee, and no transition puts one back on it.
        if (Status == TicketStatus.Closed)
        {
            return Result.Failure<TicketMessage>(TicketErrors.ClosedToMessages);
        }

        if (kind == TicketAuthorKind.Customer && visibility != TicketMessageVisibility.CustomerVisible)
        {
            return Result.Failure<TicketMessage>(TicketErrors.CustomerCannotPostInternalNote);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Result.Failure<TicketMessage>(TicketErrors.MessageBodyRequired);
        }

        if (body.Length > TicketMessage.BodyMaxLength)
        {
            return Result.Failure<TicketMessage>(TicketErrors.MessageBodyTooLong);
        }

        // Only a message the customer can actually see counts as a response to them: an internal
        // note is agents talking to each other, and letting it stop the first-response clock would
        // make the metric measurable without anybody ever replying.
        bool customerVisibleFromAgent =
            kind == TicketAuthorKind.Agent && visibility == TicketMessageVisibility.CustomerVisible;

        if (customerVisibleFromAgent)
        {
            // ??=, not =: first response means the first one. A later reply must not move it.
            FirstRespondedOnUtc ??= utcNow;
        }

        // Deliberately not routed through Reopen: that is the agent-driven "the fix did not hold"
        // transition with its own 7-day window, and a customer writing on their own resolved ticket
        // must not be refused because the window lapsed — the message would be lost with it. The
        // reopen event is raised all the same, so the two paths look identical to every consumer.
        if (Status == TicketStatus.Resolved)
        {
            Status = TicketStatus.InProgress;
            ResolvedOnUtc = null;

            Raise(new TicketReopenedDomainEvent(Id, authorId));
        }

        var message = TicketMessage.Create(Id, authorId, kind, body, visibility, utcNow);

        _messages.Add(message);

        Raise(new TicketMessagePostedDomainEvent(
            Id,
            Reference,
            message.Id,
            CustomerId,
            authorId,
            kind,
            visibility,
            Subject,
            message.Body,
            message.PostedOnUtc));

        return message;
    }

    /// <summary>
    /// The single write path for the assignee field. Internal on purpose: the only callers are the
    /// three guarded methods above, which is what makes "every assignment went through a guard" a
    /// property of the code rather than a convention — a second, direct write to this field is
    /// precisely the unguarded race the claim lock exists to close.
    /// <para>
    /// It stays visible to the unit tests as well, which use it to build tickets in the
    /// assignment-dependent states (InProgress, Resolved, Closed) without walking the whole
    /// lifecycle first.
    /// </para>
    /// </summary>
    internal void SetAssignedAgent(Guid? agentId) => AssignedAgentId = agentId;
}
