using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Locking;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Refunds;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.ApproveRefund;

internal sealed class ApproveRefundCommandHandler(
    IRefundRequestRepository refundRequestRepository,
    ISupportContext supportContext,
    IDistributedLock distributedLock,
    IDateTimeProvider dateTimeProvider,
    ISupportAuditWriter auditWriter,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ApproveRefundCommand>
{
    public async Task<Result> Handle(ApproveRefundCommand request, CancellationToken cancellationToken)
    {
        Guid adminId = supportContext.UserId;

        // Acquired BEFORE the read, like every other check-then-act in this codebase: the race
        // starts at the read, so a lock taken afterwards would still let two administrators decide
        // on the same "still Requested" snapshot. Nothing in the database would refuse the second
        // write — no aggregate here carries a concurrency token — and of all the check-then-acts in
        // this module this is the one whose double outcome is the business agreeing twice.
        await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
            SupportLocks.Refund(request.RefundRequestId),
            SupportLocks.DecisionTtl,
            cancellationToken);

        // Strands nothing: the request is still in the approval queue, so the next refresh either
        // shows it decided or offers it again.
        if (handle is null)
        {
            return Result.Failure(RefundErrors.DecisionInProgress);
        }

        RefundRequest? refundRequest =
            await refundRequestRepository.GetAsync(request.RefundRequestId, cancellationToken);

        if (refundRequest is null)
        {
            return Result.Failure(RefundErrors.NotFound(request.RefundRequestId));
        }

        // The aggregate is the rule of record for both invariants — decided once, and never by the
        // requester. The lock only makes two concurrent decisions observe them in sequence.
        Result approval = refundRequest.Approve(adminId, request.Note, dateTimeProvider.UtcNow);

        if (approval.IsFailure)
        {
            return approval;
        }

        // Keyed on the ticket, like every other entry: the audit read is per ticket, and a refund
        // decision that did not appear in the history of the case it came from would be a decision
        // nobody reviewing that case would ever see.
        auditWriter.Record(
            refundRequest.TicketId,
            SupportAuditAction.RefundApproved,
            RefundStatus.Requested.ToString(),
            RefundStatus.Approved.ToString(),
            request.Note);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
