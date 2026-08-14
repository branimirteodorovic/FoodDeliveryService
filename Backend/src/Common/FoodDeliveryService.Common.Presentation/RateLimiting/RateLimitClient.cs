using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Common.Presentation.RateLimiting;

/// <summary>
/// Who a request is being counted against — the limiter's partition key.
/// <para>
/// <b>Subject when authenticated, IP otherwise</b>, and the order matters in both directions. An IP
/// is not an identity: a whole office, a mobile carrier's NAT or a corporate VPN shares one, so
/// partitioning authenticated traffic by IP throttles a hundred innocent users because of one, and
/// counting a signed-in customer against their address rather than their account lets the same
/// account walk around the limit by moving networks. Conversely an anonymous request has nothing
/// else to be counted against — <c>users/register</c> is unauthenticated by design and is exactly
/// the endpoint an abusive client reaches for.
/// </para>
/// <para>
/// This is also why the limiter runs <b>after</b> <c>UseAuthentication()</c> in the Gateway pipeline:
/// before it, <see cref="HttpContext.User"/> is empty and every request would be an IP. The cost is
/// that a flood pays for JWT validation before being shed — signature verification against cached
/// signing keys, no I/O, and orders of magnitude cheaper than the proxied round trip it prevents.
/// </para>
/// </summary>
public static class RateLimitClient
{
    /// <summary>Prefix on a partition key derived from the authenticated subject.</summary>
    public const string SubjectPrefix = "sub";

    /// <summary>Prefix on a partition key derived from the remote address.</summary>
    public const string AddressPrefix = "ip";

    /// <summary>
    /// The bucket for a request that is neither authenticated nor attributable to an address. In
    /// practice: a connection whose remote address the server could not determine. They share one
    /// bucket deliberately — an unattributable request is the one case where being generous is the
    /// same as having no limiter at all.
    /// </summary>
    public const string UnattributedKey = "unattributed";

    /// <summary>Duende's subject claim — the same name <c>CustomClaims.Sub</c> carries service-side.</summary>
    private const string SubjectClaim = "sub";

    /// <summary>
    /// The partition key for <paramref name="context"/> — <c>sub:{subject}</c>, <c>ip:{address}</c>
    /// or <see cref="UnattributedKey"/>.
    /// </summary>
    public static string Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? subject = Subject(context.User);

        if (!string.IsNullOrWhiteSpace(subject))
        {
            return $"{SubjectPrefix}:{subject}";
        }

        string? address = context.Connection.RemoteIpAddress?.ToString();

        return string.IsNullOrWhiteSpace(address) ? UnattributedKey : $"{AddressPrefix}:{address}";
    }

    private static string? Subject(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // Duende stamps the subject as `sub`; JwtBearer's inbound claim mapping rewrites it to
        // ClaimTypes.NameIdentifier unless it is turned off. Both are read, in that order, so the
        // key does not silently fall back to the caller's IP if that setting ever moves — a limiter
        // that quietly stops partitioning by account is the failure this class exists to prevent,
        // and it would look like nothing at all in a log.
        return user.FindFirstValue(SubjectClaim) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
