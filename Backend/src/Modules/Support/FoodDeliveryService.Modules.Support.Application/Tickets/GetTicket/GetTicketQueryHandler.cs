using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicket;

internal sealed class GetTicketQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    ISupportContext supportContext)
    : IQueryHandler<GetTicketQuery, TicketResponse>
{
    public async Task<Result<TicketResponse>> Handle(GetTicketQuery request, CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        // The ownership check is in the WHERE clause, not in a branch after the read. That is what
        // makes the 404 unfakeable: a customer asking for somebody else's ticket gets no row, so
        // there is no code path on which an existence-revealing 403 could be returned instead.
        const string sql =
            $"""
             SELECT
                 t.id AS {nameof(TicketResponse.Id)},
                 t.reference AS {nameof(TicketResponse.Reference)},
                 t.customer_id AS {nameof(TicketResponse.CustomerId)},
                 t.order_id AS {nameof(TicketResponse.OrderId)},
                 t.subject AS {nameof(TicketResponse.Subject)},
                 t.category AS {nameof(TicketResponse.Category)},
                 t.priority AS {nameof(TicketResponse.Priority)},
                 t.status AS {nameof(TicketResponse.Status)},
                 t.source AS {nameof(TicketResponse.Source)},
                 t.assigned_agent_id AS {nameof(TicketResponse.AssignedAgentId)},
                 t.opened_on_utc AS {nameof(TicketResponse.OpenedOnUtc)},
                 t.first_responded_on_utc AS {nameof(TicketResponse.FirstRespondedOnUtc)},
                 t.resolved_on_utc AS {nameof(TicketResponse.ResolvedOnUtc)},
                 t.closed_on_utc AS {nameof(TicketResponse.ClosedOnUtc)}
             FROM tickets t
             WHERE t.id = @TicketId AND (@IsStaff OR t.customer_id = @UserId)
             """;

        TicketResponse? ticket = await connection.QuerySingleOrDefaultAsync<TicketResponse>(
            sql,
            new
            {
                request.TicketId,
                supportContext.UserId,
                IsStaff = TicketAccess.IsStaff(supportContext)
            });

        if (ticket is null)
        {
            return Result.Failure<TicketResponse>(TicketErrors.NotFound(request.TicketId));
        }

        return ticket;
    }
}
