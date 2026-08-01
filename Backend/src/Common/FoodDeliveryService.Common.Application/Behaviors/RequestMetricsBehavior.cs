using System.Diagnostics;
using FoodDeliveryService.Common.Application.Diagnostics;
using FoodDeliveryService.Common.Domain;
using MediatR;

namespace FoodDeliveryService.Common.Application.Behaviors;

/// <summary>
/// Records the application-boundary RED signal — one measurement per command/query, tagged by
/// request type and by the outcome derived from the returned <see cref="Result"/>. Handlers stay
/// pure: nothing in a handler knows this exists.
/// <para>
/// <b>Position matters.</b> It is registered SECOND, immediately inside
/// <c>ExceptionHandlingPipelineBehavior</c> and outside everything else. Registering it last, after
/// <c>QueryCachingBehavior</c>, would make every cache <i>hit</i> invisible — the caching behavior
/// short-circuits before the inner pipeline runs, so the recorded durations would describe cache
/// misses only, which is precisely backwards for a latency dashboard. Sitting outside the cache
/// also means the measured duration is the one the caller actually waited.
/// </para>
/// <para>
/// The <c>TResponse : Result</c> constraint is the same opt-in mechanism
/// <c>RequestLoggingPipelineBehavior</c> uses: MediatR only composes this behavior into requests
/// whose response is a <see cref="Result"/>, which in this codebase is every command and query.
/// </para>
/// </summary>
internal sealed class RequestMetricsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        string requestName = typeof(TRequest).Name;

        // Stopwatch.GetTimestamp over a Stopwatch instance: same resolution, no allocation on a path
        // that runs for every single request.
        long startedAt = Stopwatch.GetTimestamp();

        TResponse response;

        try
        {
            response = await next(cancellationToken);
        }
        catch (Exception)
        {
            // ExceptionHandlingPipelineBehavior wraps and rethrows one layer out, so this is the only
            // place a thrown request can be counted. Record, then let it continue unchanged.
            ApplicationDiagnostics.RecordException(requestName, ElapsedSeconds(startedAt));

            throw;
        }

        double elapsedSeconds = ElapsedSeconds(startedAt);

        if (response.IsSuccess)
        {
            ApplicationDiagnostics.RecordSuccess(requestName, elapsedSeconds);
        }
        else
        {
            ApplicationDiagnostics.RecordFailure(requestName, response.Error.Type, elapsedSeconds);
        }

        return response;
    }

    private static double ElapsedSeconds(long startedAt) =>
        Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
}
