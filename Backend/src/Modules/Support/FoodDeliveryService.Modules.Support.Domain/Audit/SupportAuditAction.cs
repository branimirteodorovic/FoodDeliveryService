namespace FoodDeliveryService.Modules.Support.Domain.Audit;

/// <summary>
/// The kinds of agent action the audit log records. Every member is an action a human took that
/// changed a ticket's state or its money — reads are deliberately not audited, because a log that
/// records everything is one nobody reads.
/// </summary>
public enum SupportAuditAction
{
    StatusChanged = 0,

    /// <summary>An agent took an unassigned ticket for themselves.</summary>
    Claimed = 1,

    /// <summary>Somebody put a (possibly different) agent on the ticket.</summary>
    Assigned = 2,

    Unassigned = 3,

    /// <summary>Reserved for the ticket message thread milestone.</summary>
    MessagePosted = 4,

    /// <summary>An agent asked for a customer to be refunded for an order.</summary>
    RefundRequested = 5,

    /// <summary>
    /// An administrator agreed to the refund. Never the same actor as the RefundRequested entry on
    /// the same ticket — the two rows side by side are what makes segregation of duties auditable
    /// rather than merely asserted.
    /// </summary>
    RefundApproved = 6,

    RefundRejected = 7,

    /// <summary>
    /// The money actually went back — Feature 3.8 Milestone H. <b>The first member of this enum that
    /// is not an action a human took</b>: it is written by the consumer of Payments'
    /// <c>RefundSettled</c> event, with the platform itself as the actor
    /// (<see cref="SupportAuditEntry.SystemActorId"/>).
    /// <para>
    /// It belongs in this log anyway, and arguably most of all. The log's purpose is that "an agent
    /// refunded this order" is checkable rather than asserted, and until this milestone the trail
    /// stopped at the agreement because that was where the platform stopped. The row that says the
    /// money moved is the other half of the same record, and it sits next to the request and the
    /// approval where a reviewer of the case will actually see it.
    /// </para>
    /// </summary>
    RefundSettled = 8,

    /// <summary>
    /// The approved refund moved no money, and <c>ToValue</c> carries Payments' bounded reason.
    /// Written by the platform, like <see cref="RefundSettled"/>.
    /// </summary>
    RefundFailed = 9
}
