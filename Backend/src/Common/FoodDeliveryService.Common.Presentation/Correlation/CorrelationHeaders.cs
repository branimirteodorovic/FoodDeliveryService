namespace FoodDeliveryService.Common.Presentation.Correlation;

/// <summary>
/// The correlation contract shared by every host: one header name, one <see cref="HttpContext"/>
/// item key. A support agent copies the header off a failed response and finds both the Seq logs and
/// the Jaeger trace of that request with it, because the value defaults to the W3C trace id.
/// </summary>
public static class CorrelationHeaders
{
    /// <summary>
    /// The de-facto standard header. Stamped by the Gateway, forwarded to the service that handles
    /// the request, and echoed on every response — <c>X-Request-Id</c> and <c>traceparent</c> are
    /// deliberately left alone: the first is not what any of this system's clients send, and the
    /// second is OpenTelemetry's to own.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>
    /// Where <c>CorrelationIdMiddleware</c> parks the resolved id for the rest of the pipeline, so
    /// nothing downstream has to repeat the inbound-or-generate decision (and possibly reach a
    /// different answer).
    /// </summary>
    public const string CorrelationIdItemKey = "FoodDeliveryService.CorrelationId";
}
