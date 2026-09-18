using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Locking;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.SettleRefund;

/// <summary>
/// Closes the loop an administrator's approval opened — Feature 3.8 Milestone H, §10.2.
/// <para>
/// The transition and the audit entry it records commit together, which is the invariant this
/// module was built on: the entry is staged on the unit of work and the one
/// <c>SaveChangesAsync</c> below persists both, so there is no window in which a refund is settled
/// and the case history does not say so.
/// </para>
/// <para>
/// <b>It writes the audit entry as the platform rather than as a person</b> — there is no
/// authenticated caller in the inbox job, and <c>ISupportContext.UserId</c> throws there. That is
/// what <c>RecordSystemAction</c> is for, and it is the first entry in this log that no agent
/// performed.
/// </para>
/// </summary>
internal sealed class SettleRefundCommandHandler(
    IRefundRequestRepository refundRequestRepository,
    IDistributedLock distributedLock,
    IDateTimeProvider dateTimeProvider,
    ISupportAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    ILogger<SettleRefundCommandHandler> logger)
    : ICommandHandler<SettleRefundCommand>
{
    public async Task<Result> Handle(SettleRefundCommand request, CancellationToken cancellationToken)
    {
        // The same key an administrator's decision takes, before the read, for the same reason: a
        // settlement racing a rejection, or two deliveries of one settlement racing each other,
        // would both read "Approved" and both write — and a second write here means a duplicate
        // audit entry claiming the money moved twice.
        await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
            SupportLocks.Refund(request.RefundRequestId),
            SupportLocks.DecisionTtl,
            cancellationToken);

        if (handle is null)
        {
            // A failure, unlike the decision path's: nothing re-drives this. ProcessInboxJob records
            // the error and marks the message processed (§5.6), so the reason has to land somewhere
            // an operator finds it. The money did move — Payments' own refund row is the record of
            // that — and this is only the request catching up with it.
            return Result.Failure(RefundErrors.SettlementInProgress(request.RefundRequestId));
        }

        RefundRequest? refundRequest =
            await refundRequestRepository.GetAsync(request.RefundRequestId, cancellationToken);

        if (refundRequest is null)
        {
            // Payments only ever refunds against an approval this service published, so a missing
            // request means the two databases disagree about something that happened. Worth an
            // error on the message row rather than a silent success.
            return Result.Failure(RefundErrors.NotFound(request.RefundRequestId));
        }

        if (refundRequest.Status != RefundStatus.Approved)
        {
            // A redelivery. The aggregate would absorb the call anyway; checking here is what stops
            // a second audit entry being written for the same settlement, which the aggregate cannot
            // see and which would be the more misleading of the two duplicates.
            logger.LogInformation(
                "Refund request {RefundRequestId} is {RefundStatus}, so there is no settlement to record",
                request.RefundRequestId,
                refundRequest.Status);

            return Result.Success();
        }

        Result transition = request.Settled
            ? refundRequest.Settle(dateTimeProvider.UtcNow)
            : refundRequest.MarkFailed(
                request.FailureReason ?? string.Empty,
                dateTimeProvider.UtcNow);

        if (transition.IsFailure)
        {
            return transition;
        }

        // Keyed on the ticket like every other entry, because the audit read is per ticket: a
        // refund that reached a customer's card and did not appear in the history of the case it
        // came from would be invisible to the next agent to open it.
        auditWriter.RecordSystemAction(
            refundRequest.TicketId,
            request.Settled ? SupportAuditAction.RefundSettled : SupportAuditAction.RefundFailed,
            RefundStatus.Approved.ToString(),
            request.Settled ? RefundStatus.Settled.ToString() : request.FailureReason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
