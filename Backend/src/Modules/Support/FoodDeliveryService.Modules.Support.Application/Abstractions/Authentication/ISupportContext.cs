namespace FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;

/// <summary>
/// The authenticated caller, as the Support module needs it. Two jobs, and both of them are
/// security decisions rather than conveniences:
///
/// 1. <see cref="UserId"/> is where a ticket owner and an acting agent come from. A request body
///    never names either — a customer id taken from the body would let anyone open (or read) a
///    ticket as somebody else, and an actor id taken from the body would make the audit log
///    (assignment milestone) worthless.
/// 2. <see cref="HasPermission"/> is the ownership bypass: an agent holding
///    <c>support-tickets:manage</c> sees every ticket, a customer sees only their own.
/// </summary>
public interface ISupportContext
{
    /// <summary>The caller's user id (the JWT sub claim).</summary>
    Guid UserId { get; }

    /// <summary>
    /// True when the caller's resolved permission set contains the given code. Permission claims
    /// are added per request by CustomClaimsTransformation, so this is an in-memory check.
    /// </summary>
    bool HasPermission(string permissionCode);
}
