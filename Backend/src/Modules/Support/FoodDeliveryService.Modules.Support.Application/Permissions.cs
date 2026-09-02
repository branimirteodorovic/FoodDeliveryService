namespace FoodDeliveryService.Modules.Support.Application;

/// <summary>
/// Permission codes used by this module's endpoints. They must match the codes seeded in the Users
/// service (Users.Domain Permission + PermissionConfiguration) — permissions are resolved at
/// request time via GetUserPermissionsRequest and enforced by the permission policy provider.
///
/// The namespace is <c>support-*</c>, not <c>tickets:*</c>. The bare <c>tickets:*</c> codes existed
/// in Permission.cs as leftovers from an event-ticketing scaffold and were granted to every
/// Customer; reusing them would have handed support-agent access to the entire customer base. That
/// scaffold was deleted in the permissions milestone and these codes stand on their own.
/// </summary>
public static class Permissions
{
    /// <summary>Customer: open a ticket, and reply on their own. Administrators hold it too.</summary>
    public const string OpenTicket = "support-tickets:open";

    /// <summary>
    /// Agent: read any ticket. Customer: read their own — the narrowing happens in the handler,
    /// because one permission code cannot express "yours only".
    /// </summary>
    public const string GetTickets = "support-tickets:read";

    /// <summary>
    /// Agent/administrator: status transitions, internal notes, the audit log. Never held by a
    /// customer, so it doubles as the marker that the caller is staff.
    /// </summary>
    public const string ManageTickets = "support-tickets:manage";

    /// <summary>
    /// Agent: claim a ticket out of the queue, and hand one back. Administrator: additionally
    /// assign a ticket to somebody other than themselves, which is gated on
    /// <see cref="AdministerTickets"/> below rather than on this code.
    /// </summary>
    public const string AssignTickets = "support-tickets:assign";

    /// <summary>
    /// The administrator ownership bypass, mirroring <c>deliveries:administer</c>: the marker that
    /// separates an administrator from an agent, both of whom hold every other code here.
    /// <para>
    /// A dedicated code rather than reusing <c>refunds:approve</c> — the only other admin-only
    /// support permission. Inferring "is an administrator" from the refund permission would silently
    /// hand out ticket-routing authority the day a senior agent is granted refund approval, and a
    /// privilege that leaks through an unrelated grant is exactly the trap the <c>support-*</c>
    /// namespace was carved out to avoid.
    /// </para>
    /// </summary>
    public const string AdministerTickets = "support-tickets:administer";

    /// <summary>
    /// Agent (and administrator): raise a refund request against a ticket's order, and read the
    /// request queue. Asking is not deciding — see <see cref="ApproveRefund"/>.
    /// </summary>
    public const string RequestRefund = "refunds:request";

    /// <summary>
    /// Administrator only, and the reason the refund workflow has two steps at all. Deliberately a
    /// separate code from <see cref="RequestRefund"/> rather than a stronger version of it: an
    /// account that holds both can still not decide its own request, because
    /// <c>RefundRequest.Approve</c> refuses the requester regardless of permissions.
    /// </summary>
    public const string ApproveRefund = "refunds:approve";
}
