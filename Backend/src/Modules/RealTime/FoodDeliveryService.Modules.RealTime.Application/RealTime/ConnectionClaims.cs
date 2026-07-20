using System.Security.Claims;

namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// Reads the connecting principal's identity from its JWT claims. Group membership is derived from
/// these claims and never from anything the client sends, so this is the single trusted source of
/// the caller's id inside the hub.
/// </summary>
public static class ConnectionClaims
{
    // The module-side user id. CustomClaimsTransformation adds this "sub" claim after resolving the
    // user via the Users service (the raw Duende token's "sub" is remapped to NameIdentifier), so it
    // is the SAME id space as the CustomerId carried on Orders/Delivery integration events — which is
    // exactly what makes user:{sub} the correct fan-out target for a customer's own orders.
    private const string Sub = "sub";

    /// <summary>
    /// The module-side user id of the connected caller. Throws when the principal carries no
    /// resolved <c>sub</c> claim, so an unresolved connection joins no group and is rejected cleanly.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal? principal)
    {
        string? userId = principal?.FindFirst(Sub)?.Value;

        return Guid.TryParse(userId, out Guid parsedUserId)
            ? parsedUserId
            : throw new Common.Application.Exceptions.ApplicationException("User identifier is unavailable");
    }
}
