using FoodDeliveryService.Modules.Support.Domain.Refunds;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.GetRefundRequests;

/// <summary>
/// One row of the approval queue. A response DTO, never the entity (hard rule #3).
/// <para>
/// The agent and administrator <em>names</em> are joined from the local replica so the queue reads
/// as "Jane Doe asked, Sam Patel approved" without a call to Users. Both are nullable: the join is a
/// LEFT JOIN, because an actor whose registration event has not been consumed yet must still show
/// their decision rather than dropping the row.
/// </para>
/// </summary>
public sealed record RefundRequestResponse
{
    public Guid Id { get; init; }

    public Guid TicketId { get; init; }

    /// <summary>The ticket's human-quotable reference, so the queue links back to the case.</summary>
    public string TicketReference { get; init; }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public decimal Amount { get; init; }

    public string Reason { get; init; }

    public RefundStatus Status { get; init; }

    public Guid RequestedByAgentId { get; init; }

    public string? RequestedByAgentName { get; init; }

    public Guid? DecidedByAdminId { get; init; }

    public string? DecidedByAdminName { get; init; }

    public string? DecisionNote { get; init; }

    public DateTime RequestedOnUtc { get; init; }

    public DateTime? DecidedOnUtc { get; init; }
}
