using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// Stamps the platform's security response headers on <b>every</b> response — Feature 3.7 Milestone D.
/// <para>
/// The headers are written from <see cref="HttpResponse.OnStarting(Func{Task})"/> rather than before
/// calling the next middleware, and that is the load-bearing detail. A header set on the way in is
/// lost the moment something resets the response, which is exactly what <c>GlobalExceptionHandler</c>
/// and the <c>429</c> rejection path do — so a naive implementation decorates the 200s and leaves
/// every error response bare, which is the half a scanner looks at. <c>OnStarting</c> runs after the
/// last writer and before the first byte, so a 500, a <c>ProblemDetails</c> 400 and a plain 404 all
/// carry the same headers as a 200.
/// </para>
/// <para>
/// Headers are set with the indexer rather than appended: a host that later adds its own value must
/// end up with one well-formed header, not two contradictory ones.
/// </para>
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersOptions options)
{
    private const string ContentTypeOptions = "X-Content-Type-Options";
    private const string FrameOptions = "X-Frame-Options";
    private const string ReferrerPolicy = "Referrer-Policy";
    private const string ContentSecurityPolicy = "Content-Security-Policy";
    private const string StrictTransportSecurity = "Strict-Transport-Security";

    public Task Invoke(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Captured now: by the time OnStarting runs, a re-executing error handler may have rewritten
        // the path (UseExceptionHandler re-executes the pipeline), and the documentation carve-out
        // must follow the path the client actually asked for.
        bool isDocumentation = options.IsDocumentationPath(context.Request.Path.Value);
        bool isHttps = context.Request.IsHttps;

        context.Response.OnStarting(() =>
        {
            IHeaderDictionary headers = context.Response.Headers;

            headers[ContentTypeOptions] = "nosniff";
            headers[FrameOptions] = "DENY";
            headers[ReferrerPolicy] = "no-referrer";

            headers[ContentSecurityPolicy] = isDocumentation
                ? options.DocumentationContentSecurityPolicy
                : options.ContentSecurityPolicy;

            // HSTS over plain HTTP is not merely useless, it is actively harmful *here*: nothing in
            // this repository terminates TLS (docker-compose and the KinD manifests are HTTP-only by
            // design), so a browser that honoured it would pin itself to a scheme the local platform
            // does not serve and the whole stack would become unreachable from that browser until
            // its HSTS cache was cleared by hand. Emitting it only when the request already arrived
            // over HTTPS means it appears exactly where a TLS-terminating proxy sits in front — and
            // that is also why the Gateway trusts X-Forwarded-Proto (§5.2): without forwarded
            // headers, a proxied HTTPS request looks like HTTP here and never gets the header.
            if (isHttps)
            {
                headers[StrictTransportSecurity] = options.StrictTransportSecurityValue;
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
