namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// The response headers every host stamps, from the <c>"SecurityHeaders"</c> configuration section.
/// <para>
/// The defaults are the ones an API that returns nothing but JSON can afford: deny everything, then
/// carve out the one surface that genuinely needs to execute script — the API documentation UI. They
/// are configurable because a deployment behind a different edge may want a different CSP, not
/// because any host is expected to relax them.
/// </para>
/// </summary>
public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    /// <summary>
    /// On by default, and the kill switch is for diagnosing a broken client rather than for a
    /// deployment choice. Turning it off is visible in the startup log for that reason.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The policy for the API surface. An endpoint that returns <c>application/json</c> loads no
    /// script, no style, no image and no font, and is never framed — so the honest policy is
    /// <c>'none'</c> for everything. <c>frame-ancestors</c> is the half a browser actually enforces
    /// for a document; the rest costs nothing and is correct.
    /// </summary>
    public string ContentSecurityPolicy { get; set; } = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>
    /// The policy for <see cref="DocumentationPathPrefixes"/>. Swagger UI and Scalar are real HTML
    /// pages that bootstrap themselves from an inline script and inline styles, so the API policy
    /// above renders them blank — a self-inflicted breakage that looks exactly like a broken build.
    /// This is deliberately the *only* carve-out, and it is scoped by path rather than by
    /// environment.
    /// </summary>
    public string DocumentationContentSecurityPolicy { get; set; } =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";

    /// <summary>
    /// Paths served the documentation policy, matched as case-insensitive prefixes.
    /// <para>
    /// <b>Nothing maps these yet.</b> The hosts map <c>/openapi</c> (a JSON document, which would be
    /// fine under the strict policy) and no UI at all; Milestone G is what adds Swagger UI and
    /// Scalar. The carve-out ships first on purpose — a CSP added after a UI exists breaks the UI in
    /// the PR that adds the CSP, and a CSP added before it is one line of configuration the UI
    /// simply works under.
    /// </para>
    /// </summary>
    public string[] DocumentationPathPrefixes { get; set; } = ["/swagger", "/scalar", "/docs", "/openapi"];

    /// <summary>
    /// <c>Strict-Transport-Security</c> max-age. Emitted <b>only</b> over HTTPS — see
    /// <see cref="SecurityHeadersMiddleware"/> for why that matters here more than usual.
    /// </summary>
    public int StrictTransportSecurityMaxAgeDays { get; set; } = 365;

    /// <summary>Whether the HSTS header carries <c>includeSubDomains</c>.</summary>
    public bool StrictTransportSecurityIncludeSubDomains { get; set; } = true;

    /// <summary>The rendered <c>Strict-Transport-Security</c> value.</summary>
    public string StrictTransportSecurityValue =>
        $"max-age={(int)TimeSpan.FromDays(Math.Max(StrictTransportSecurityMaxAgeDays, 0)).TotalSeconds}" +
        (StrictTransportSecurityIncludeSubDomains ? "; includeSubDomains" : string.Empty);

    /// <summary>
    /// True when <paramref name="path"/> is one of the documentation surfaces and should be served
    /// <see cref="DocumentationContentSecurityPolicy"/>.
    /// <para>
    /// The match is on whole path <b>segments</b>, not on characters: a bare
    /// <c>StartsWith("/swagger")</c> also matches <c>/swaggerish</c>, and a carve-out that leaks onto
    /// a neighbouring route is a relaxed CSP on an endpoint nobody meant to relax.
    /// </para>
    /// </summary>
    public bool IsDocumentationPath(string? path) =>
        !string.IsNullOrEmpty(path) && Array.Exists(DocumentationPathPrefixes, prefix =>
            path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
}
