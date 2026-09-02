using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Application.Analytics.GetSupportSummary;

/// <summary>
/// Six aggregate statements over one connection, sent as a single command with
/// <c>QueryMultiple</c>: the sections are independent, and six round trips to build one screen is
/// the shape that makes a dashboard feel slow long before the SQL does.
/// <para>
/// Every duration is computed in Postgres and CAST to <c>double precision</c> rather than left as
/// the <c>numeric</c> that <c>EXTRACT</c> returns — <c>numeric</c> arrives as a <c>decimal</c> and
/// would have to be converted on the way into a <c>double?</c>. Counts are CAST to <c>integer</c>
/// for the same reason: <c>COUNT(*)</c> is a <c>bigint</c>.
/// </para>
/// <para>
/// The window is half-open, <c>&gt;= @FromUtc AND &lt; @ToUtc</c>, in every statement. A closed
/// upper bound double-counts a ticket landing exactly on a boundary that two adjacent reports
/// share, and monthly reports are precisely the ones whose boundaries line up.
/// </para>
/// </summary>
internal sealed class GetSupportSummaryQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<GetSupportSummaryQuery, SupportSummaryResponse>
{
    /// <summary>
    /// The six sections, in the order the handler reads them back. One constant rather than six so
    /// the reading order and the statement order cannot drift — <c>QueryMultiple</c> maps result
    /// sets positionally and would otherwise hand the day series to the category reader without
    /// complaining.
    /// </summary>
    private const string Sql =
        $"""
         -- 1. Headline totals. `resolved` is keyed on when a ticket was RESOLVED and `responded` on
         -- when it was OPENED, deliberately: throughput belongs to the window the work finished in,
         -- while a first-response time belongs to the ticket that was waiting for it.
         WITH resolved AS (
             SELECT CAST(EXTRACT(EPOCH FROM (t.resolved_on_utc - t.opened_on_utc)) AS double precision) AS seconds
             FROM tickets t
             WHERE t.resolved_on_utc >= @FromUtc
               AND t.resolved_on_utc < @ToUtc
         ),
         responded AS (
             SELECT CAST(EXTRACT(EPOCH FROM (t.first_responded_on_utc - t.opened_on_utc)) AS double precision) AS seconds
             FROM tickets t
             WHERE t.opened_on_utc >= @FromUtc
               AND t.opened_on_utc < @ToUtc
               AND t.first_responded_on_utc IS NOT NULL
         )
         SELECT
             CAST((
                 SELECT COUNT(*) FROM tickets t
                 WHERE t.opened_on_utc >= @FromUtc AND t.opened_on_utc < @ToUtc
             ) AS integer) AS {nameof(SupportSummaryTotals.TicketsOpened)},
             CAST((SELECT COUNT(*) FROM resolved) AS integer) AS {nameof(SupportSummaryTotals.TicketsResolved)},
             CAST((SELECT COUNT(*) FROM responded) AS integer) AS {nameof(SupportSummaryTotals.TicketsFirstResponded)},
             (SELECT AVG(seconds) FROM resolved) AS {nameof(SupportSummaryTotals.AverageResolutionSeconds)},
             (SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY seconds) FROM resolved)
                 AS {nameof(SupportSummaryTotals.MedianResolutionSeconds)},
             (SELECT AVG(seconds) FROM responded) AS {nameof(SupportSummaryTotals.AverageFirstResponseSeconds)},
             (SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY seconds) FROM responded)
                 AS {nameof(SupportSummaryTotals.MedianFirstResponseSeconds)};

         -- 2. Tickets per day, gap-filled. generate_series drives the rows, so a day with no
         -- activity is a row of zeroes and not an absence; a GROUP BY over the tickets alone would
         -- skip it and the chart would draw a straight line across the gap. The series is built in
         -- plain UTC timestamps and the timestamptz columns are converted to match, rather than
         -- relying on whatever the session's TimeZone happens to be.
         SELECT
             -- Kept as a timestamp rather than CAST to `date`: Npgsql reads a `date` column as
             -- a DateOnly, and Dapper then wants a constructor taking one — the series is already
             -- midnight-aligned, so the cast buys nothing and costs the mapping.
             d.day AS {nameof(SupportDailyCount.Date)},
             CAST((
                 SELECT COUNT(*) FROM tickets t
                 WHERE t.opened_on_utc AT TIME ZONE 'UTC' >= d.day
                   AND t.opened_on_utc AT TIME ZONE 'UTC' < d.day + INTERVAL '1 day'
             ) AS integer) AS {nameof(SupportDailyCount.Opened)},
             CAST((
                 SELECT COUNT(*) FROM tickets t
                 WHERE t.resolved_on_utc AT TIME ZONE 'UTC' >= d.day
                   AND t.resolved_on_utc AT TIME ZONE 'UTC' < d.day + INTERVAL '1 day'
             ) AS integer) AS {nameof(SupportDailyCount.Resolved)}
         FROM generate_series(
             date_trunc('day', @FromUtc AT TIME ZONE 'UTC'),
             date_trunc('day', (@ToUtc AT TIME ZONE 'UTC') - INTERVAL '1 microsecond'),
             INTERVAL '1 day') AS d(day)
         ORDER BY d.day;

         -- 3. By category — what the queue is actually made of, which is a product signal before it
         -- is a staffing one.
         SELECT
             t.category AS {nameof(SupportCategoryCount.Category)},
             CAST(COUNT(*) AS integer) AS {nameof(SupportCategoryCount.Opened)},
             CAST(COUNT(*) FILTER (WHERE t.resolved_on_utc IS NOT NULL) AS integer)
                 AS {nameof(SupportCategoryCount.Resolved)}
         FROM tickets t
         WHERE t.opened_on_utc >= @FromUtc
           AND t.opened_on_utc < @ToUtc
         GROUP BY t.category
         ORDER BY 2 DESC, 1;

         -- 4. By status: where those same tickets stand NOW. A snapshot of the backlog, so a large
         -- Open count beside a healthy resolved total is a queue growing faster than it drains.
         SELECT
             t.status AS {nameof(SupportStatusCount.Status)},
             CAST(COUNT(*) AS integer) AS {nameof(SupportStatusCount.Count)}
         FROM tickets t
         WHERE t.opened_on_utc >= @FromUtc
           AND t.opened_on_utc < @ToUtc
         GROUP BY t.status
         ORDER BY 1;

         -- 5. By agent, with the name from the local replica (hard rule #5 — never a call to Users).
         -- LEFT JOIN: an agent whose registration event has not been projected yet keeps their row
         -- with a null name, because dropping it would understate work that was actually done.
         SELECT
             t.assigned_agent_id AS {nameof(SupportAgentWorkload.AgentId)},
             CASE
                 WHEN a.id IS NULL THEN NULL
                 ELSE a.first_name || ' ' || a.last_name
             END AS {nameof(SupportAgentWorkload.AgentName)},
             CAST(COUNT(*) AS integer) AS {nameof(SupportAgentWorkload.Assigned)},
             CAST(COUNT(*) FILTER (WHERE t.resolved_on_utc IS NOT NULL) AS integer)
                 AS {nameof(SupportAgentWorkload.Resolved)}
         FROM tickets t
         LEFT JOIN support_agents a ON a.id = t.assigned_agent_id
         WHERE t.opened_on_utc >= @FromUtc
           AND t.opened_on_utc < @ToUtc
           AND t.assigned_agent_id IS NOT NULL
         GROUP BY t.assigned_agent_id, a.id, a.first_name, a.last_name
         ORDER BY 3 DESC, 1;

         -- 6. Refunds by outcome, keyed on when they were REQUESTED so a request and its decision
         -- stay in the same window. The amount is a reporting total: no money moved.
         SELECT
             r.status AS {nameof(SupportRefundTotal.Status)},
             CAST(COUNT(*) AS integer) AS {nameof(SupportRefundTotal.Count)},
             COALESCE(SUM(r.amount), 0) AS {nameof(SupportRefundTotal.TotalAmount)}
         FROM refund_requests r
         WHERE r.requested_on_utc >= @FromUtc
           AND r.requested_on_utc < @ToUtc
         GROUP BY r.status
         ORDER BY 1;
         """;

    public async Task<Result<SupportSummaryResponse>> Handle(
        GetSupportSummaryQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        var parameters = new { request.FromUtc, request.ToUtc };

        await using SqlMapper.GridReader reader = await connection.QueryMultipleAsync(
            new CommandDefinition(Sql, parameters, cancellationToken: cancellationToken));

        // Read in statement order. Each ReadAsync advances the grid, so these six lines are the
        // only place the section order is expressed on this side of the wire.
        SupportSummaryTotals totals = await reader.ReadSingleAsync<SupportSummaryTotals>();
        List<SupportDailyCount> perDay = [.. await reader.ReadAsync<SupportDailyCount>()];
        List<SupportCategoryCount> byCategory = [.. await reader.ReadAsync<SupportCategoryCount>()];
        List<SupportStatusCount> byStatus = [.. await reader.ReadAsync<SupportStatusCount>()];
        List<SupportAgentWorkload> byAgent = [.. await reader.ReadAsync<SupportAgentWorkload>()];
        List<SupportRefundTotal> refunds = [.. await reader.ReadAsync<SupportRefundTotal>()];

        return new SupportSummaryResponse
        {
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            Totals = totals,
            TicketsPerDay = perDay,
            ByCategory = byCategory,
            ByStatus = byStatus,
            ByAgent = byAgent,
            Refunds = refunds
        };
    }
}
