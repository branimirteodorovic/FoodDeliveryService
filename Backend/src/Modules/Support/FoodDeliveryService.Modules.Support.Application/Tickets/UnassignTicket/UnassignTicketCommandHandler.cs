using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Locking;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.UnassignTicket;

internal sealed class UnassignTicketCommandHandler(
    ITicketsRepository ticketsRepository,
    ISupportContext supportContext,
    IDistributedLock distributedLock,
    ISupportAuditWriter auditWriter,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UnassignTicketCommand>
{
    public async Task<Result> Handle(UnassignTicketCommand request, CancellationToken cancellationToken)
    {
        // Under the same key as claim and assign. Unassigning writes the very field the other two
        // race over, so leaving it outside the lock would reopen the race from a third door.
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

        Guid? previousAgentId = ticket.AssignedAgentId;

        Result unassign = ticket.Unassign(supportContext.UserId, request.Reason);

        if (unassign.IsFailure)
        {
            return unassign;
        }

        auditWriter.Record(
            ticket.Id,
            SupportAuditAction.Unassigned,
            previousAgentId?.ToString(),
            toValue: null,
            request.Reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
