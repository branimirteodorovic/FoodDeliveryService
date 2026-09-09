namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Authentication;

/// <summary>
/// The authenticated caller, as the Payments module needs it. Two jobs, and both of them are
/// security decisions rather than conveniences:
///
/// 1. <see cref="UserId"/> is the customer a saved card and a payment belong to. A request body
///    never names one — a customer id taken from the body would let anyone attach a card to, or
///    read the payments of, somebody else's account.
/// 2. <see cref="HasPermission"/> is the ownership bypass: an administrator holding
///    <c>payments:administer</c> reads any payment, a customer reads only their own.
/// </summary>
public interface IPaymentsContext
{
    /// <summary>The caller's user id (the JWT sub claim).</summary>
    Guid UserId { get; }

    /// <summary>
    /// True when the caller's resolved permission set contains the given code. Permission claims
    /// are added per request by CustomClaimsTransformation, so this is an in-memory check.
    /// </summary>
    bool HasPermission(string permissionCode);
}
