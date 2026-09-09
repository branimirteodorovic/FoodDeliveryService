namespace FoodDeliveryService.Modules.Payments.Application;

/// <summary>
/// Permission codes used by this module's endpoints. They must match the codes seeded in the Users
/// service (Users.Domain <c>Permission</c> + <c>PermissionConfiguration</c>) — permissions are
/// resolved at request time via <c>GetUserPermissionsRequest</c> and enforced by the permission
/// policy provider. Milestone A seeded all three; the endpoints that carry them arrive in §6 and
/// later.
///
/// The namespace is its own. Widening an existing support code — <c>refunds:approve</c> in
/// particular — to also mean "can see payments" is the privilege leak the <c>support-*</c>
/// namespace was carved out to avoid, and it would hand payment visibility to any senior agent
/// granted refund approval.
/// </summary>
public static class Permissions
{
    /// <summary>Customer: attach and detach their own cards. Administrators hold it too.</summary>
    public const string ManagePaymentMethods = "payment-methods:manage";

    /// <summary>
    /// Customer: read their own payments. Administrator: read any — the narrowing happens in the
    /// handler, because one permission code cannot express "yours only".
    /// </summary>
    public const string GetPayments = "payments:read";

    /// <summary>
    /// The administrator ownership bypass, mirroring <c>deliveries:administer</c> and
    /// <c>support-tickets:administer</c>: read any payment, force-release an authorization. Never
    /// held by a customer, a support agent, a restaurant manager or a driver — an agent who needs
    /// to see a payment gets it through the ticket context, not a direct grant.
    /// </summary>
    public const string AdministerPayments = "payments:administer";
}
