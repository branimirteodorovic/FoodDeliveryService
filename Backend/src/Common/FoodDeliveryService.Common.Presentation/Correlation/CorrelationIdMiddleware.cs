using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Common.Presentation.Correlation;

/// <summary>
/// Resolves one <c>X-Correlation-Id</c> per request and makes it visible in the three places a
/// support agent needs it: on the request as it travels downstream (the Gateway stamps it, YARP
/// copies request headers to the proxied call, so the service that actually handles the request sees
/// the same id and preserves it), on the response (the id the agent copies out of a failed call),
/// and in <see cref="HttpContext.Items"/> for <see cref="LogContextTraceLoggingMiddleware"/> to push
/// into the Serilog scope.
/// <para>
/// The id <b>defaults to the W3C trace id</b> rather than a fresh scheme of its own — that is the
/// whole point: the same string finds the Seq logs and the Jaeger trace, so nothing has to join two
/// id spaces. A generated GUID is only the last resort for a request that has no ambient
/// <see cref="Activity"/> at all (no tracing configured, or a probe hit before the pipeline is up).
/// </para>
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Long enough for a 32-character trace id, a GUID or a caller's own request id, short enough
    /// that a hostile client cannot push kilobytes into every log line and response header.
    /// </summary>
    private const int MaxLength = 128;

    public Task Invoke(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string correlationId = Resolve(context);

        // Overwrite, never append: an inbound header that survived Resolve is written back
        // unchanged, and anything else is replaced, so exactly one well-formed value is forwarded.
        context.Request.Headers[CorrelationHeaders.CorrelationId] = correlationId;

        context.Items[CorrelationHeaders.CorrelationIdItemKey] = correlationId;

        // Set on OnStarting rather than now: an exception handler that resets the response (see
        // GlobalExceptionHandler) would otherwise drop the header from exactly the 500 responses
        // whose id someone is most likely to want.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeaders.CorrelationId] = correlationId;

            return Task.CompletedTask;
        });

        return next(context);
    }

    private static string Resolve(HttpContext context)
    {
        string inbound = context.Request.Headers[CorrelationHeaders.CorrelationId].ToString();

        if (IsWellFormed(inbound))
        {
            return inbound;
        }

        // The default: this request's trace id, so the correlation id and the Jaeger trace are the
        // same string. Note the id is per REQUEST, not per hop — every service on the path reuses
        // the one the Gateway stamped, because it arrives on the header above.
        string? traceId = Activity.Current?.TraceId.ToString();

        return string.IsNullOrEmpty(traceId) ? Guid.NewGuid().ToString("N") : traceId;
    }

    /// <summary>
    /// An inbound id is echoed into response headers and into every log line of the request, so it
    /// is accepted only in the shape the platform itself produces: a bounded run of ASCII letters,
    /// digits and the few separators real request ids use. Anything else — a header repeated so the
    /// value arrives comma-joined, control characters, an oversized value — is not rejected as an
    /// error, it is simply replaced by a generated id, because a malformed correlation header is not
    /// a reason to fail a customer's request.
    /// </summary>
    private static bool IsWellFormed(string value)
    {
        if (value.Length is 0 or > MaxLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool allowed = char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':';

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
