using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Domain.Refunds;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.GetRefundRequests;

/// <summary>
/// One fixed statement with the status filter optional, the same shape as the ticket queue: Postgres
/// plans it once, and there is no branch on which a filter could be concatenated rather than bound.
/// The optional parameter is CAST explicitly because Npgsql cannot infer the type of a bare NULL.
/// </summary>
internal sealed class GetRefundRequestsQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<GetRefundRequestsQuery, IReadOnlyCollection<RefundRequestResponse>>
{
    public async Task<Result<IReadOnlyCollection<RefundRequestResponse>>> Handle(
        GetRefundRequestsQuery request,
        CancellationToken cancellationToken)
    {
        // An unrecognized status narrows to nothing rather than being ignored: serving the whole
        // queue confidently for `?status=Nonsense` is the wrong answer, not a lenient one.
        int? status = ParseOrSentinel(request.Status);

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        // LEFT JOINs on both actors: a decision whose administrator is not yet in the agent replica
        // must still appear with its amount and timestamp. Dropping a refund row to tidy up a
        // missing name would hide exactly the record this feature exists to keep.
        const string sql =
            $"""
             SELECT
                 r.id AS {nameof(RefundRequestResponse.Id)},
                 r.ticket_id AS {nameof(RefundRequestResponse.TicketId)},
                 r.ticket_reference AS {nameof(RefundRequestResponse.TicketReference)},
                 r.order_id AS {nameof(RefundRequestResponse.OrderId)},
                 r.customer_id AS {nameof(RefundRequestResponse.CustomerId)},
                 r.amount AS {nameof(RefundRequestResponse.Amount)},
                 r.reason AS {nameof(RefundRequestResponse.Reason)},
                 r.status AS {nameof(RefundRequestResponse.Status)},
                 r.requested_by_agent_id AS {nameof(RefundRequestResponse.RequestedByAgentId)},
                 CASE
                     WHEN ag.id IS NULL THEN NULL
                     ELSE ag.first_name || ' ' || ag.last_name
                 END AS {nameof(RefundRequestResponse.RequestedByAgentName)},
                 r.decided_by_admin_id AS {nameof(RefundRequestResponse.DecidedByAdminId)},
                 CASE
                     WHEN ad.id IS NULL THEN NULL
                     ELSE ad.first_name || ' ' || ad.last_name
                 END AS {nameof(RefundRequestResponse.DecidedByAdminName)},
                 r.decision_note AS {nameof(RefundRequestResponse.DecisionNote)},
                 r.requested_on_utc AS {nameof(RefundRequestResponse.RequestedOnUtc)},
                 r.decided_on_utc AS {nameof(RefundRequestResponse.DecidedOnUtc)}
             FROM refund_requests r
             LEFT JOIN support_agents ag ON ag.id = r.requested_by_agent_id
             LEFT JOIN support_agents ad ON ad.id = r.decided_by_admin_id
             WHERE CAST(@Status AS integer) IS NULL OR r.status = CAST(@Status AS integer)
             ORDER BY r.requested_on_utc DESC, r.id DESC
             LIMIT @Take OFFSET @Skip
             """;

        IEnumerable<RefundRequestResponse> refundRequests = await connection.QueryAsync<RefundRequestResponse>(
            sql,
            new
            {
                Status = status,
                Take = request.PageSize,
                Skip = (request.Page - 1) * request.PageSize
            });

        return refundRequests.ToList();
    }

    // -1 is a value no enum member has, so an unrecognized filter matches nothing.
    private static int? ParseOrSentinel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse(value, ignoreCase: true, out RefundStatus parsed) ? (int)parsed : -1;
    }
}
