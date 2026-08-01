using System.Diagnostics.Metrics;
using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Common.Application.Diagnostics;

/// <summary>
/// The application-boundary RED signal — rate, errors, duration per command/query — recorded by
/// <c>RequestMetricsBehavior</c> for every request that goes through the MediatR pipeline.
/// <para>
/// It is deliberately NOT the same thing as the transport-level <c>http.server.request.duration</c>
/// the ASP.NET Core instrumentation already gives us: that one is keyed by route and counts an
/// HTTP status, this one is keyed by request type and counts the <see cref="Result"/> a handler
/// returned. A business failure that correctly answers 400 is a success to the transport signal and
/// a failure here, and a request that never came over HTTP at all (an outbox job sending a command)
/// only exists here.
/// </para>
/// <para>
/// <c>AddInfrastructure</c> registers <see cref="Name"/> through <c>AddMeter</c>, so every module
/// host collects these. A host that called <c>AddApplication</c> without <c>AddInfrastructure</c>
/// would emit them into nothing — no such host exists, and the two calls sit next to each other in
/// every Program.cs.
/// </para>
/// </summary>
public static class ApplicationDiagnostics
{
    /// <summary>The meter name <c>AddInfrastructure</c> must pass to <c>AddMeter</c>.</summary>
    public const string Name = "FoodDeliveryService.Application";

    /// <summary>
    /// The request type name (<c>PlaceOrderCommand</c>). Bounded by the number of commands and
    /// queries in the solution — the one tag on this meter that grows with the codebase, and it
    /// grows by one series per handler, not per call.
    /// </summary>
    private const string RequestTagName = "request";

    private const string OutcomeTagName = "outcome";

    /// <summary>
    /// The <see cref="ErrorType"/> of the failure, or <see cref="ExceptionOutcome"/> for a request
    /// that threw. Five enum values plus one — never an error code, never a message.
    /// </summary>
    private const string ErrorTypeTagName = "error.type";

    private const string SuccessOutcome = "success";
    private const string FailureOutcome = "failure";
    private const string ExceptionOutcome = "exception";

    private static readonly AppDiagnostics Diagnostics = new(Name);

    private static readonly Counter<long> Requests = Diagnostics.Meter.CreateCounter<long>(
        "app.requests",
        unit: "{request}",
        description: "Commands and queries handled by the application pipeline.");

    private static readonly Histogram<double> Duration = Diagnostics.Meter.CreateHistogram<double>(
        "app.request.duration",
        unit: "s",
        description: "Time a command or query spent inside the application pipeline.");

    private static readonly Counter<long> Failures = Diagnostics.Meter.CreateCounter<long>(
        "app.request.failures",
        unit: "{request}",
        description: "Commands and queries that returned a failure Result or threw.");

    /// <summary>
    /// Seconds, not milliseconds: it matches the unit OpenTelemetry's own HTTP histograms use, so a
    /// dashboard can put the two latencies on one axis without a conversion in the query.
    /// </summary>
    public static void RecordSuccess(string requestName, double durationInSeconds)
    {
        var request = new KeyValuePair<string, object?>(RequestTagName, requestName);
        var outcome = new KeyValuePair<string, object?>(OutcomeTagName, SuccessOutcome);

        Requests.Add(1, request, outcome);
        Duration.Record(durationInSeconds, request, outcome);
    }

    /// <summary>
    /// A handler that answered <c>Result.Failure</c> — a rejected transition, a failed validation, a
    /// missing row. Counted in <c>app.requests</c> as well, so the error <i>ratio</i> a RED panel
    /// draws has the same denominator as the rate.
    /// </summary>
    public static void RecordFailure(string requestName, ErrorType errorType, double durationInSeconds) =>
        RecordFailure(requestName, FailureOutcome, errorType.ToString(), durationInSeconds);

    /// <summary>
    /// A request that threw. <c>ExceptionHandlingPipelineBehavior</c> sits OUTSIDE this behavior and
    /// rethrows rather than converting to a failure <see cref="Result"/>, so without recording here
    /// the one class of error most worth alerting on would be missing from every RED panel.
    /// </summary>
    public static void RecordException(string requestName, double durationInSeconds) =>
        RecordFailure(requestName, ExceptionOutcome, ExceptionOutcome, durationInSeconds);

    private static void RecordFailure(
        string requestName,
        string outcomeValue,
        string errorTypeValue,
        double durationInSeconds)
    {
        var request = new KeyValuePair<string, object?>(RequestTagName, requestName);
        var outcome = new KeyValuePair<string, object?>(OutcomeTagName, outcomeValue);

        Requests.Add(1, request, outcome);
        Duration.Record(durationInSeconds, request, outcome);
        Failures.Add(1, request, new KeyValuePair<string, object?>(ErrorTypeTagName, errorTypeValue));
    }
}
