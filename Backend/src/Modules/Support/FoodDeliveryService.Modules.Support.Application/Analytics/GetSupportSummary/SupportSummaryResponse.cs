using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Analytics.GetSupportSummary;

/// <summary>
/// The management summary for one window. A response DTO built from six aggregate reads, never an
/// entity (hard rule #3) — there is no entity to expose here in any case.
/// <para>
/// The window it was computed over is echoed back rather than left implicit. A caller that sent no
/// bounds got the default fortnight-or-so, and a chart drawn from this has to be able to label its
/// own axis without re-deriving what the server decided.
/// </para>
/// </summary>
public sealed record SupportSummaryResponse
{
    public DateTime FromUtc { get; init; }

    public DateTime ToUtc { get; init; }

    public SupportSummaryTotals Totals { get; init; } = new();

    /// <summary>
    /// Gap-filled: a day on which nothing happened is a row of zeroes, not a missing row. A chart
    /// fed the sparse version silently draws a straight line between the two days either side of a
    /// quiet Sunday, which reads as steady traffic rather than none.
    /// </summary>
    public IReadOnlyCollection<SupportDailyCount> TicketsPerDay { get; init; } = [];

    public IReadOnlyCollection<SupportCategoryCount> ByCategory { get; init; } = [];

    public IReadOnlyCollection<SupportStatusCount> ByStatus { get; init; } = [];

    public IReadOnlyCollection<SupportAgentWorkload> ByAgent { get; init; } = [];

    public IReadOnlyCollection<SupportRefundTotal> Refunds { get; init; } = [];
}

/// <summary>
/// The headline numbers. Every duration is nullable because a window in which nothing was resolved
/// has no resolution time — reporting zero there would say the queue is instant rather than empty.
/// </summary>
public sealed record SupportSummaryTotals
{
    /// <summary>Tickets whose <c>OpenedOnUtc</c> falls in the window.</summary>
    public int TicketsOpened { get; init; }

    /// <summary>
    /// Tickets whose <c>ResolvedOnUtc</c> falls in the window — not a subset of
    /// <see cref="TicketsOpened"/>. A ticket opened before the window and resolved inside it counts
    /// here and not there, which is what makes the two numbers answer different questions ("how
    /// much arrived" against "how much was got through") rather than one being the other's share.
    /// </summary>
    public int TicketsResolved { get; init; }

    /// <summary>Tickets opened in the window that have had a customer-visible agent reply.</summary>
    public int TicketsFirstResponded { get; init; }

    public double? AverageResolutionSeconds { get; init; }

    /// <summary>
    /// Reported alongside the average for the same reason the load-test report leads with p95: one
    /// week-old ticket drags a mean far enough to hide what the typical customer experienced.
    /// </summary>
    public double? MedianResolutionSeconds { get; init; }

    /// <summary>Opened → first customer-visible agent reply. What support teams are measured on.</summary>
    public double? AverageFirstResponseSeconds { get; init; }

    public double? MedianFirstResponseSeconds { get; init; }
}

/// <param name="Date">The UTC day, midnight-aligned.</param>
/// <param name="Opened">Tickets opened that day.</param>
/// <param name="Resolved">Tickets resolved that day, whenever they were opened.</param>
public sealed record SupportDailyCount(DateTime Date, int Opened, int Resolved);

public sealed record SupportCategoryCount(TicketCategory Category, int Opened, int Resolved);

/// <summary>Where the tickets opened in the window stand <em>now</em> — a snapshot, not a flow.</summary>
public sealed record SupportStatusCount(TicketStatus Status, int Count);

/// <param name="AgentName">
/// Joined from the local replica, and nullable: the join is a LEFT JOIN so an agent whose
/// registration event has not been projected yet still shows their workload rather than dropping
/// out of the report.
/// </param>
public sealed record SupportAgentWorkload(Guid AgentId, string? AgentName, int Assigned, int Resolved);

/// <param name="TotalAmount">
/// Summed for reporting only. Nothing moves money on the back of it — the platform has no payment
/// processing, so this is what was agreed to, not what was paid.
/// </param>
public sealed record SupportRefundTotal(RefundStatus Status, int Count, decimal TotalAmount);
