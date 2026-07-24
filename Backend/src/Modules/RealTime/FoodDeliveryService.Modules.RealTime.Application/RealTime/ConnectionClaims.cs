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

    // Mirrors Common.Infrastructure.Authentication.CustomClaims.Permission's value. Duplicated
    // (rather than referenced) for the same reason Sub is: Application must not depend on
    // Infrastructure.
    private const string Permission = "permission";

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

    /// <summary>
    /// Whether the connected caller's claims include the given permission code (see
    /// <see cref="Permissions"/>) — the signal <c>TrackingHub.OnConnectedAsync</c> (Milestone D) uses
    /// to decide whether to attempt joining the restaurant or support dashboard group.
    /// </summary>
    public static bool HasPermission(this ClaimsPrincipal? principal, string permission) =>
        principal?.HasClaim(Permission, permission) ?? false;
}
