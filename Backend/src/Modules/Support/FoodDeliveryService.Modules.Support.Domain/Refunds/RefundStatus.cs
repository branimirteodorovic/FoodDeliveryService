namespace FoodDeliveryService.Modules.Support.Domain.Refunds;

/// <summary>
/// Where a refund request stands. Explicit values: the number is persisted on every row, so a
/// member may be appended but never renumbered or reordered.
/// <para>
/// There is no <c>Paid</c> member, and its absence is the design rather than an omission. This
/// platform processes no payments: a refund request is a record of a decision a human made, and
/// nothing downstream moves money when it is approved.
/// </para>
/// </summary>
public enum RefundStatus
{
    /// <summary>An agent has asked for the refund; an administrator has not yet decided.</summary>
    Requested = 0,

    Approved = 1,

    Rejected = 2
}
