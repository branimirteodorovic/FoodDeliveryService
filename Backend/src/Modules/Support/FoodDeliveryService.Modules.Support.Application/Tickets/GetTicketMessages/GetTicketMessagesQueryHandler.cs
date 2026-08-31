using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketMessages;

/// <summary>
/// The customer-facing read of a support thread, and the one place internal notes could leak.
/// <para>
/// They are excluded <b>in the SQL</b>, not in the mapper: a projection that fetches notes and drops
/// them on the way out is one refactor — one added field, one reused DTO, one "let's return the raw
/// rows" — away from publishing agent-to-agent commentary to the customer it is about. Rows that
/// never leave Postgres cannot be leaked by C# that has not been written yet.
/// </para>
/// </summary>
internal sealed class GetTicketMessagesQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    ISupportContext supportContext)
    : IQueryHandler<GetTicketMessagesQuery, IReadOnlyCollection<TicketMessageResponse>>
{
    public async Task<Result<IReadOnlyCollection<TicketMessageResponse>>> Handle(
        GetTicketMessagesQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        bool isStaff = TicketAccess.IsStaff(supportContext);

        // The ticket is checked separately, with the same ownership predicate every other read uses:
        // a thread with no messages yet is an empty list, and a ticket belonging to somebody else is
        // a 404 — inferring either from an empty row set would collapse two different answers into
        // one, and the wrong one at that.
        const string ticketSql =
            """
            SELECT COUNT(*)
            FROM tickets t
            WHERE t.id = @TicketId AND (@IsStaff OR t.customer_id = @UserId)
            """;

        int ticketCount = await connection.ExecuteScalarAsync<int>(
            ticketSql,
            new { request.TicketId, supportContext.UserId, IsStaff = isStaff });

        if (ticketCount == 0)
        {
            return Result.Failure<IReadOnlyCollection<TicketMessageResponse>>(
                TicketErrors.NotFound(request.TicketId));
        }

        // LEFT JOIN for the same reason the audit read uses one: an agent whose replica has not
        // arrived yet costs the message its author's name, never the message itself.
        const string sql =
            $"""
             SELECT
                 m.id AS {nameof(TicketMessageResponse.Id)},
                 m.ticket_id AS {nameof(TicketMessageResponse.TicketId)},
                 m.author_id AS {nameof(TicketMessageResponse.AuthorId)},
                 m.author_kind AS {nameof(TicketMessageResponse.AuthorKind)},
                 CASE
                     WHEN ag.id IS NULL THEN NULL
                     ELSE ag.first_name || ' ' || ag.last_name
                 END AS {nameof(TicketMessageResponse.AuthorName)},
                 m.body AS {nameof(TicketMessageResponse.Body)},
                 m.visibility AS {nameof(TicketMessageResponse.Visibility)},
                 m.posted_on_utc AS {nameof(TicketMessageResponse.PostedOnUtc)}
             FROM ticket_messages m
             LEFT JOIN support_agents ag ON ag.id = m.author_id
             WHERE m.ticket_id = @TicketId
               AND (@IsStaff OR m.visibility = @CustomerVisible)
             ORDER BY m.posted_on_utc, m.id
             """;

        IEnumerable<TicketMessageResponse> messages = await connection.QueryAsync<TicketMessageResponse>(
            sql,
            new
            {
                request.TicketId,
                IsStaff = isStaff,
                CustomerVisible = (int)TicketMessageVisibility.CustomerVisible
            });

        return messages.ToList();
    }
}
