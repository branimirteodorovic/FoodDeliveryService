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

    /// <summary>Reserved for the refund workflow milestone.</summary>
    RefundRequested = 5,

    /// <summary>Reserved for the refund workflow milestone.</summary>
    RefundApproved = 6,

    /// <summary>Reserved for the refund workflow milestone.</summary>
    RefundRejected = 7
}
