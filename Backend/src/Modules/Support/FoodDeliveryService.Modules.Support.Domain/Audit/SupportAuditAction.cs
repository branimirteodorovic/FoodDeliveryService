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

    RefundRejected = 7
}
