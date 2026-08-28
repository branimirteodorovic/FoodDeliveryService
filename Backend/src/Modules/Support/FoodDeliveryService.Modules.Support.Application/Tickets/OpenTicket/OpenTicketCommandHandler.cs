using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.OpenTicket;

internal sealed class OpenTicketCommandHandler(
    ITicketsRepository ticketsRepository,
    ISupportContext supportContext,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork)
    : ICommandHandler<OpenTicketCommand, Guid>
{
    public async Task<Result<Guid>> Handle(OpenTicketCommand request, CancellationToken cancellationToken)
    {
        // Naming another customer is a staff action. A customer sending OnBehalfOfCustomerId is
        // told the ticket was not opened rather than having it silently rewritten to their own id,
        // because silently ignoring the field would hide a client bug that looks like data loss.
        bool onBehalfOfSomeoneElse = request.OnBehalfOfCustomerId is { } customerId && customerId != Guid.Empty;

        if (onBehalfOfSomeoneElse && !TicketAccess.IsStaff(supportContext))
        {
            return Result.Failure<Guid>(SupportErrors.NotAuthorizedToActOnBehalfOfCustomer);
        }

        Guid ticketCustomerId = onBehalfOfSomeoneElse ? request.OnBehalfOfCustomerId!.Value : supportContext.UserId;

        TicketSource source = onBehalfOfSomeoneElse ? TicketSource.AgentCreated : TicketSource.CustomerPortal;

        // Allocated before Create so the reference is part of the aggregate from birth — the
        // sequence hands out its number outside the transaction, so a failed insert burns a number
        // rather than handing the same one to the next caller.
        string reference = await ticketsRepository.NextReferenceAsync(cancellationToken);

        Result<Ticket> ticket = Ticket.Create(
            reference,
            ticketCustomerId,
            request.OrderId,
            request.Subject,
            Enum.Parse<TicketCategory>(request.Category, ignoreCase: true),
            source,
            dateTimeProvider.UtcNow);

        if (ticket.IsFailure)
        {
            return Result.Failure<Guid>(ticket.Error);
        }

        ticketsRepository.Insert(ticket.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ticket.Value.Id;
    }
}
