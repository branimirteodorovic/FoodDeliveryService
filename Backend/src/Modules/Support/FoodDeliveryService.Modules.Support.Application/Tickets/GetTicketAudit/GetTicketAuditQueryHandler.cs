using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketAudit;

/// <summary>
/// The accountability read: everything that was done to one ticket, newest first. Staff-only — the
/// endpoint requires <c>support-tickets:manage</c>, which a customer never holds — so there is no
/// ownership predicate here, and deliberately no customer-visible variant of this projection.
/// </summary>
internal sealed class GetTicketAuditQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<GetTicketAuditQuery, IReadOnlyCollection<SupportAuditEntryResponse>>
{
    public async Task<Result<IReadOnlyCollection<SupportAuditEntryResponse>>> Handle(
        GetTicketAuditQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        // The ticket is checked separately rather than inferred from an empty entry list: a ticket
        // that exists but has had nothing done to it has an empty audit log, and returning a 404 for
        // it would be a different — and wrong — answer.
        const string ticketExistsSql = "SELECT COUNT(*) FROM tickets WHERE id = @TicketId";

        int ticketCount = await connection.ExecuteScalarAsync<int>(ticketExistsSql, new { request.TicketId });

        if (ticketCount == 0)
        {
            return Result.Failure<IReadOnlyCollection<SupportAuditEntryResponse>>(
                TicketErrors.NotFound(request.TicketId));
        }

        // LEFT JOIN, not JOIN: an entry whose actor is not in the agent replica (an administrator
        // acting before their registration event was consumed, say) must still appear. Dropping a
        // row from an audit log to tidy up a missing name would defeat the point of keeping one.
        const string sql =
            $"""
             SELECT
                 a.id AS {nameof(SupportAuditEntryResponse.Id)},
                 a.ticket_id AS {nameof(SupportAuditEntryResponse.TicketId)},
                 a.actor_id AS {nameof(SupportAuditEntryResponse.ActorId)},
                 CASE
                     WHEN ag.id IS NULL THEN NULL
                     ELSE ag.first_name || ' ' || ag.last_name
                 END AS {nameof(SupportAuditEntryResponse.ActorName)},
                 a.action AS {nameof(SupportAuditEntryResponse.Action)},
                 a.from_value AS {nameof(SupportAuditEntryResponse.FromValue)},
                 a.to_value AS {nameof(SupportAuditEntryResponse.ToValue)},
                 a.reason AS {nameof(SupportAuditEntryResponse.Reason)},
                 a.occurred_on_utc AS {nameof(SupportAuditEntryResponse.OccurredOnUtc)}
             FROM support_audit_entries a
             LEFT JOIN support_agents ag ON ag.id = a.actor_id
             WHERE a.ticket_id = @TicketId
             ORDER BY a.occurred_on_utc DESC, a.id DESC
             """;

        IEnumerable<SupportAuditEntryResponse> entries =
            await connection.QueryAsync<SupportAuditEntryResponse>(sql, new { request.TicketId });

        return entries.ToList();
    }
}
