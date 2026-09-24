namespace FoodDeliveryService.Modules.Users.Application.Abstractions.Authentication;

/// <summary>
/// The authenticated caller, as the Users module needs it — the same abstraction every other module
/// carries (<c>IOrdersContext</c>, <c>IPaymentsContext</c>, …), for the same reason: a handler in the
/// Application layer cannot reach <c>HttpContext</c>, and the caller's identity must never be taken
/// from a request parameter.
/// <para>
/// Only <c>UserId</c>, deliberately. The other modules also expose a <c>HasPermission</c> hook for
/// their administrator ownership bypass (<c>deliveries:administer</c> and friends); Users has no
/// such bypass to express today — the one self-scoped read here is self-scoped with no exceptions —
/// and an unused bypass is an invitation to route a lookup through it.
/// </para>
/// </summary>
public interface IUsersContext
{
    /// <summary>
    /// The caller's <b>module-side</b> user id — the <c>users.id</c> primary key, published as the
    /// <c>sub</c> custom claim by <c>CustomClaimsTransformation</c> after it resolves the Duende
    /// identity id against this module's database. Not the identity id in the raw token.
    /// </summary>
    Guid UserId { get; }
}
