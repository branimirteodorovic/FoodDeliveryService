using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Locking;
using FoodDeliveryService.Modules.Support.Domain.Agents;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.AssignTicket;

internal sealed class AssignTicketCommandHandler(
    ITicketsRepository ticketsRepository,
    ISupportAgentRepository agentRepository,
    ISupportContext supportContext,
    IDistributedLock distributedLock,
    ISupportAuditWriter auditWriter,
    IUnitOfWork unitOfWork)
    : ICommandHandler<AssignTicketCommand>
{
    public async Task<Result> Handle(AssignTicketCommand request, CancellationToken cancellationToken)
    {
        Guid actorId = supportContext.UserId;

        // Self-or-admin, the same shape Delivery uses for reading somebody else's driver profile.
        // Checked before anything is read, so an unauthorized caller learns nothing about the ticket.
        if (request.AgentId != actorId && !TicketAccess.IsAdministrator(supportContext))
        {
            return Result.Failure(SupportErrors.NotAuthorizedToAssignAnotherAgent);
        }

        // The replica is what makes this checkable without asking Users (hard rule #5). Without it,
        // an assignment to a nonexistent or non-agent user would leave a ticket that nobody owns and
        // that no unassigned-queue filter would ever show again.
        SupportAgentReplica? agent = await agentRepository.GetAsync(request.AgentId, cancellationToken);

        if (agent is null || !agent.IsActive)
        {
            return Result.Failure(SupportErrors.AgentNotFound(request.AgentId));
        }

        // The same key as the claim path, and for the same reason: this is check-then-act on the
        // assignee. Sharing the key is the point — an admin assignment racing an agent's claim must
        // be serialized against it, which two different key names would not do.
        await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
            SupportLocks.Ticket(request.TicketId),
            SupportLocks.ClaimTtl,
            cancellationToken);

        if (handle is null)
        {
            return Result.Failure(TicketErrors.ClaimInProgress);
        }

        Ticket? ticket = await ticketsRepository.GetAsync(request.TicketId, cancellationToken);

        if (ticket is null)
        {
            return Result.Failure(TicketErrors.NotFound(request.TicketId));
        }

        // Read before the transition: afterwards the field holds the new agent, and the outgoing one
        // is the half of a reassignment that says who the ticket was taken away from.
        Guid? previousAgentId = ticket.AssignedAgentId;

        Result assign = ticket.AssignTo(request.AgentId, actorId);

        if (assign.IsFailure)
        {
            return assign;
        }

        auditWriter.Record(
            ticket.Id,
            SupportAuditAction.Assigned,
            previousAgentId?.ToString(),
            request.AgentId.ToString(),
            request.Reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
