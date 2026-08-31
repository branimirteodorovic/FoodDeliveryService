using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.PostTicketMessage;

/// <summary>
/// Turns "the caller" into an author: the id comes from the token and the kind from the permission
/// set, and neither is a field a client can send. Everything after that is the aggregate's decision.
/// </summary>
internal sealed class PostTicketMessageCommandHandler(
    ITicketsRepository ticketsRepository,
    ISupportContext supportContext,
    IDateTimeProvider dateTimeProvider,
    ISupportAuditWriter auditWriter,
    IUnitOfWork unitOfWork)
    : ICommandHandler<PostTicketMessageCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PostTicketMessageCommand request, CancellationToken cancellationToken)
    {
        bool isStaff = TicketAccess.IsStaff(supportContext);

        var visibility = Enum.Parse<TicketMessageVisibility>(request.Visibility, ignoreCase: true);

        // Refused, not silently downgraded to CustomerVisible: an author who believes their note is
        // internal and finds it in the customer's thread is a worse failure than a rejected request.
        // The aggregate refuses a customer-authored note as well, on data-integrity grounds — this
        // is the authorization half of the same rule, and it answers before anything is read.
        if (visibility == TicketMessageVisibility.InternalNote && !isStaff)
        {
            return Result.Failure<Guid>(SupportErrors.NotAuthorizedToPostInternalNote);
        }

        Ticket? ticket = await ticketsRepository.GetAsync(request.TicketId, cancellationToken);

        // Ownership failure returns NotFound, not a forbidden: the same rule every read in this
        // module follows, because a 403 tells a customer probing ticket ids that one of them exists.
        if (ticket is null || !isStaff && ticket.CustomerId != supportContext.UserId)
        {
            return Result.Failure<Guid>(TicketErrors.NotFound(request.TicketId));
        }

        // Captured before the post: a message on a resolved ticket moves it to InProgress, and the
        // "from" half of that would otherwise be gone by the time the audit entry is written.
        TicketStatus statusBefore = ticket.Status;

        Result<TicketMessage> message = ticket.PostMessage(
            supportContext.UserId,
            isStaff ? TicketAuthorKind.Agent : TicketAuthorKind.Customer,
            request.Body,
            visibility,
            dateTimeProvider.UtcNow);

        if (message.IsFailure)
        {
            return Result.Failure<Guid>(message.Error);
        }

        // The visibility, not the body: the audit log is a record of what was done, and copying a
        // customer's message into a second table only widens where it has to be protected.
        auditWriter.Record(
            ticket.Id,
            SupportAuditAction.MessagePosted,
            toValue: visibility.ToString());

        // A post that resumed a resolved ticket changed its status, and a status change with no
        // entry is exactly the hole this log exists to close — the audit reader must not have to
        // know that MessagePosted can silently mean "and it was reopened".
        if (ticket.Status != statusBefore)
        {
            auditWriter.Record(
                ticket.Id,
                SupportAuditAction.StatusChanged,
                statusBefore.ToString(),
                ticket.Status.ToString());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return message.Value.Id;
    }
}
