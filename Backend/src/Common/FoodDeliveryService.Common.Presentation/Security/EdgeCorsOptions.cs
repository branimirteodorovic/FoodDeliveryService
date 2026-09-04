namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// The browser policy for the single public entry point, from the <c>"Cors"</c> configuration
/// section.
/// <para>
/// <b>Empty by default, and that is the safe default</b>: with no origins configured the policy
/// matches nothing, no <c>Access-Control-Allow-Origin</c> is emitted and a browser refuses the
/// cross-origin call — which is the correct behaviour for a base <c>appsettings.json</c> that ships
/// to every environment. A server-to-server caller is unaffected either way; CORS is enforced by
/// browsers, never by the server.
/// </para>
/// </summary>
public sealed class EdgeCorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>The policy name. One named policy, applied to every proxied route.</summary>
    public const string PolicyName = "fooddeliveryservice-spa";

    /// <summary>
    /// Exact origins (scheme + host + port, no trailing slash) the SPA is served from —
    /// <c>Frontend/FRONTEND_PLAN.md</c> names this as its one backend prerequisite.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>
    /// Whether the browser may send credentials (cookies, and the <c>Authorization</c> header on a
    /// SignalR handshake). True because <c>hubs/**</c> needs it: the negotiate request carries the
    /// access token and a WebSocket cannot set headers, so the SignalR client falls back to a query
    /// string only when credentials are allowed.
    /// <para>
    /// The framework <b>throws at startup</b> if this is combined with a wildcard origin, which is
    /// the one place this can go wrong loudly instead of quietly — see
    /// <see cref="EdgeCorsExtensions"/>, which refuses the combination itself with a better message.
    /// </para>
    /// </summary>
    public bool AllowCredentials { get; set; } = true;

    /// <summary>
    /// Response headers the browser lets the SPA read. Without this list a cross-origin caller can
    /// see neither of the two headers the platform expects it to act on: the correlation id to put
    /// in a bug report, and the <c>Retry-After</c> the edge limiter sends with a 429.
    /// </summary>
    public string[] ExposedHeaders { get; set; } = ["X-Correlation-Id", "Retry-After"];

    /// <summary>How long a browser may cache a preflight response.</summary>
    public int PreflightMaxAgeSeconds { get; set; } = 600;

    /// <summary>True when at least one origin is configured — i.e. the policy can match anything.</summary>
    public bool HasOrigins => AllowedOrigins.Length > 0;
}
