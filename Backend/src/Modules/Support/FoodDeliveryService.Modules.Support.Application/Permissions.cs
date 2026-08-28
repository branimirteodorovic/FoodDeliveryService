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
}
