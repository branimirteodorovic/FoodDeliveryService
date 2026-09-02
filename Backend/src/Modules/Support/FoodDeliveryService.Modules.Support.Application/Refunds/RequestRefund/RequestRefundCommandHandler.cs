using System.Globalization;
using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Orders;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.RequestRefund;

/// <summary>
/// Gathers the three facts the aggregate is not allowed to fetch for itself — the ticket's order,
/// that order's subtotal, and whether the order already has a live request — and hands them in.
/// Every rule about what they mean stays in <see cref="RefundRequest.Create"/>.
/// </summary>
internal sealed class RequestRefundCommandHandler(
    ITicketsRepository ticketsRepository,
    IOrderSnapshotRepository orderSnapshotRepository,
    IRefundRequestRepository refundRequestRepository,
    ISupportContext supportContext,
    IDateTimeProvider dateTimeProvider,
    ISupportAuditWriter auditWriter,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RequestRefundCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RequestRefundCommand request, CancellationToken cancellationToken)
    {
        Ticket? ticket = await ticketsRepository.GetAsync(request.TicketId, cancellationToken);

        if (ticket is null)
        {
            return Result.Failure<Guid>(TicketErrors.NotFound(request.TicketId));
        }

        // Refused early and explicitly, rather than falling through to an order lookup for a null
        // id: "this ticket is not about an order" is a different answer from "that order is unknown
        // here", and an agent needs to be able to tell them apart.
        if (ticket.OrderId is not { } orderId)
        {
            return Result.Failure<Guid>(RefundErrors.TicketHasNoOrder);
        }

        // The replica, never a call to Orders (hard rule #5). A missing snapshot means Support has
        // not consumed that order's OrderPlaced event, and the honest answer is to refuse: the
        // alternative is an unbounded refund approved because a projection had not caught up.
        OrderSnapshot? order = await orderSnapshotRepository.GetAsync(orderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<Guid>(RefundErrors.OrderNotFound(orderId));
        }

        bool orderHasActiveRefundRequest =
            await refundRequestRepository.HasActiveForOrderAsync(orderId, cancellationToken);

        Result<RefundRequest> refundRequest = RefundRequest.Create(
            ticket.Id,
            ticket.Reference,
            ticket.OrderId,
            ticket.CustomerId,
            request.Amount,
            order.Subtotal,
            orderHasActiveRefundRequest,
            request.Reason,

            // From the token. An audit log whose actor the request body names is not evidence.
            supportContext.UserId,
            dateTimeProvider.UtcNow);

        if (refundRequest.IsFailure)
        {
            return Result.Failure<Guid>(refundRequest.Error);
        }

        refundRequestRepository.Insert(refundRequest.Value);

        // Invariant culture on the amount: this string is a record, read back by whoever audits the
        // ticket, and a decimal separator that depends on the server's locale is a corrupted one.
        auditWriter.Record(
            ticket.Id,
            SupportAuditAction.RefundRequested,
            toValue: refundRequest.Value.Amount.ToString("F2", CultureInfo.InvariantCulture),
            reason: request.Reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return refundRequest.Value.Id;
    }
}
