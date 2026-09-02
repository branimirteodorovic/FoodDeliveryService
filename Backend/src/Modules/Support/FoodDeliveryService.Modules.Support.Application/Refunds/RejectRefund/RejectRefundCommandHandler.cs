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

namespace FoodDeliveryService.Modules.Support.Application.Refunds.RejectRefund;

internal sealed class RejectRefundCommandHandler(
    IRefundRequestRepository refundRequestRepository,
    ISupportContext supportContext,
    IDistributedLock distributedLock,
    IDateTimeProvider dateTimeProvider,
    ISupportAuditWriter auditWriter,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RejectRefundCommand>
{
    public async Task<Result> Handle(RejectRefundCommand request, CancellationToken cancellationToken)
    {
        Guid adminId = supportContext.UserId;

        // The same key and TTL as the approval, and that is the point: the two decisions contend
        // with each other, so a separate key per verb would serialize neither against the other and
        // one request could end up both approved and rejected.
        await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
            SupportLocks.Refund(request.RefundRequestId),
            SupportLocks.DecisionTtl,
            cancellationToken);

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

        Result rejection = refundRequest.Reject(adminId, request.Note, dateTimeProvider.UtcNow);

        if (rejection.IsFailure)
        {
            return rejection;
        }

        auditWriter.Record(
            refundRequest.TicketId,
            SupportAuditAction.RefundRejected,
            RefundStatus.Requested.ToString(),
            RefundStatus.Rejected.ToString(),
            request.Note);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
