using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Locking;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ClaimTicket;

internal sealed class ClaimTicketCommandHandler(
    ITicketsRepository ticketsRepository,
    ISupportContext supportContext,
    IDistributedLock distributedLock,
    ISupportAuditWriter auditWriter,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ClaimTicketCommand>
{
    public async Task<Result> Handle(ClaimTicketCommand request, CancellationToken cancellationToken)
    {
        Guid agentId = supportContext.UserId;

        // Acquired BEFORE the read, not around the write. The check-then-act begins at the read: a
        // lock taken after it would still let two agents decide on the same stale "unassigned"
        // snapshot and both write, and the tickets table carries no concurrency token that would
        // refuse the second one.
        await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
            SupportLocks.Ticket(request.TicketId),
            SupportLocks.ClaimTtl,
            cancellationToken);

        // Strands nothing — the ticket is still in the queue, so the next refresh either shows it
        // taken or offers it again. This is the failure a support UI retries.
        if (handle is null)
        {
            return Result.Failure(TicketErrors.ClaimInProgress);
        }

        Ticket? ticket = await ticketsRepository.GetAsync(request.TicketId, cancellationToken);

        if (ticket is null)
        {
            return Result.Failure(TicketErrors.NotFound(request.TicketId));
        }

        // The aggregate guard is the rule of record; the lock above only makes two concurrent
        // claims observe it in sequence.
        Result claim = ticket.Claim(agentId);

        if (claim.IsFailure)
        {
            return claim;
        }

        // Staged before the save, so the assignment and the record of it commit together. This is
        // also what the concurrency test asserts on: exactly one audit row for one ticket proves the
        // lock held, in a way that a pair of HTTP status codes does not.
        auditWriter.Record(
            ticket.Id,
            SupportAuditAction.Claimed,
            toValue: agentId.ToString());

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
