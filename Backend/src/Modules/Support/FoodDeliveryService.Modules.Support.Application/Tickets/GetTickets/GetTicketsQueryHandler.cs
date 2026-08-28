using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTickets;

/// <summary>
/// One SQL statement with every filter optional, rather than a StringBuilder assembling a WHERE
/// clause: the shape is fixed, so Postgres can plan it once, and there is no branch on which a
/// filter could be concatenated instead of parameterized.
///
/// Every optional parameter is CAST explicitly. Npgsql cannot infer the type of a bare NULL, and an
/// unfiltered call sends nulls for most of them.
/// </summary>
internal sealed class GetTicketsQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    ISupportContext supportContext)
    : IQueryHandler<GetTicketsQuery, IReadOnlyCollection<TicketSummaryResponse>>
{
    public async Task<Result<IReadOnlyCollection<TicketSummaryResponse>>> Handle(
        GetTicketsQuery request,
        CancellationToken cancellationToken)
    {
        // An unparseable filter value narrows to nothing rather than being ignored: silently
        // returning the unfiltered queue for `?status=Nonsense` would be the wrong answer served
        // confidently. The validator rejects malformed values before this, so this is the backstop.
        int? status = ParseOrSentinel<TicketStatus>(request.Status);
        int? category = ParseOrSentinel<TicketCategory>(request.Category);

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT
                 t.id AS {nameof(TicketSummaryResponse.Id)},
                 t.reference AS {nameof(TicketSummaryResponse.Reference)},
                 t.customer_id AS {nameof(TicketSummaryResponse.CustomerId)},
                 t.order_id AS {nameof(TicketSummaryResponse.OrderId)},
                 t.subject AS {nameof(TicketSummaryResponse.Subject)},
                 t.category AS {nameof(TicketSummaryResponse.Category)},
                 t.priority AS {nameof(TicketSummaryResponse.Priority)},
                 t.status AS {nameof(TicketSummaryResponse.Status)},
                 t.assigned_agent_id AS {nameof(TicketSummaryResponse.AssignedAgentId)},
                 t.opened_on_utc AS {nameof(TicketSummaryResponse.OpenedOnUtc)},
                 t.resolved_on_utc AS {nameof(TicketSummaryResponse.ResolvedOnUtc)}
             FROM tickets t
             WHERE (@IsStaff OR t.customer_id = @UserId)
               AND (CAST(@Status AS integer) IS NULL OR t.status = CAST(@Status AS integer))
               AND (CAST(@Category AS integer) IS NULL OR t.category = CAST(@Category AS integer))
               AND (CAST(@AssignedAgentId AS uuid) IS NULL OR t.assigned_agent_id = CAST(@AssignedAgentId AS uuid))
               AND (NOT @Unassigned OR t.assigned_agent_id IS NULL)
               AND (CAST(@From AS timestamptz) IS NULL OR t.opened_on_utc >= CAST(@From AS timestamptz))
               AND (CAST(@To AS timestamptz) IS NULL OR t.opened_on_utc <= CAST(@To AS timestamptz))
             ORDER BY t.priority DESC, t.opened_on_utc DESC
             LIMIT @Take OFFSET @Skip
             """;

        IEnumerable<TicketSummaryResponse> tickets = await connection.QueryAsync<TicketSummaryResponse>(
            sql,
            new
            {
                supportContext.UserId,
                IsStaff = TicketAccess.IsStaff(supportContext),
                Status = status,
                Category = category,
                request.AssignedAgentId,
                request.Unassigned,
                request.From,
                request.To,
                Take = request.PageSize,
                Skip = (request.Page - 1) * request.PageSize
            });

        return tickets.ToList();
    }

    // -1 is a value no enum member has, so an unrecognized filter matches nothing.
    private static int? ParseOrSentinel<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? Convert.ToInt32(parsed, null) : -1;
    }
}
