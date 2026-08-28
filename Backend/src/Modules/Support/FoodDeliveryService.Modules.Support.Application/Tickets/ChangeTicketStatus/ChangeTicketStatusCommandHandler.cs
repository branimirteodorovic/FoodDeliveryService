using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ChangeTicketStatus;

// Pure orchestration: pick the aggregate method that matches the requested target state and let it
// decide. No status is compared here beyond choosing between the two roads to InProgress, and even
// that choice is re-checked by the method it lands on.
internal sealed class ChangeTicketStatusCommandHandler(
    ITicketsRepository ticketsRepository,
    ISupportContext supportContext,
    IDateTimeProvider dateTimeProvider,
    ISupportAuditWriter auditWriter,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ChangeTicketStatusCommand>
{
    public async Task<Result> Handle(ChangeTicketStatusCommand request, CancellationToken cancellationToken)
    {
        Ticket? ticket = await ticketsRepository.GetAsync(request.TicketId, cancellationToken);

        if (ticket is null)
        {
            return Result.Failure(TicketErrors.NotFound(request.TicketId));
        }

        Guid actorId = supportContext.UserId;
        DateTime utcNow = dateTimeProvider.UtcNow;

        var target = Enum.Parse<TicketStatus>(request.Status, ignoreCase: true);

        // Captured before the transition, because afterwards the field holds the new value and the
        // "from" half of the audit entry would be lost.
        TicketStatus from = ticket.Status;

        Result transition = target switch
        {
            // Two roads reach InProgress. Coming back from Resolved is a reopen — a different
            // event, a different rule (the 7-day window) and it clears ResolvedOnUtc — so the
            // ticket's current state, not the caller, decides which one this is.
            TicketStatus.InProgress when ticket.Status == TicketStatus.Resolved =>
                ticket.Reopen(actorId, utcNow),
            TicketStatus.InProgress => ticket.StartProgress(actorId),
            TicketStatus.Resolved => ticket.Resolve(actorId, request.Reason ?? string.Empty, utcNow),
            TicketStatus.Escalated => ticket.Escalate(actorId, request.Reason ?? string.Empty),
            TicketStatus.Closed => ticket.Close(actorId, utcNow),

            // Open is a birth state, not a destination: a ticket that needs work again goes to
            // InProgress via Reopen, which keeps the agent who already has the context.
            _ => Result.Failure(TicketErrors.InvalidTransition(ticket.Status, target))
        };

        if (transition.IsFailure)
        {
            return transition;
        }

        // In the same SaveChangesAsync as the transition itself, not in a domain-event handler: the
        // outbox runs on its own schedule, so a handler-written entry could fail after the status
        // change had already committed — a ticket whose history has a hole in it is exactly what
        // this log exists to prevent. The status recorded is the one the aggregate ended up in
        // rather than the one requested: a reopen is asked for as InProgress, and only the ticket's
        // own state decides which of the two roads there it actually took.
        auditWriter.Record(
            ticket.Id,
            SupportAuditAction.StatusChanged,
            from.ToString(),
            ticket.Status.ToString(),
            request.Reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
