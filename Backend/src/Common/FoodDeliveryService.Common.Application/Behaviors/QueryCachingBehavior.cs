using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Domain;
using MediatR;

namespace FoodDeliveryService.Common.Application.Behaviors;

// The TRequest : ICachedQuery constraint means MediatR only wires this behavior into requests
// that opt in — every other query/command passes straight through, same mechanism
// RequestLoggingPipelineBehavior uses with its `where TResponse : Result` constraint.
//
// Caches the whole Result<T> response rather than just the unwrapped value: TResponse is only
// known here as `Result`, not the closed Result<T>, so there is no compile-time hook to pull the
// inner value out generically. That's safe because a Result<T> success round-trips through JSON
// (Value/IsSuccess/Error all map onto the record's constructor parameters) and a failure is never
// written to the cache in the first place — the one case where Value's throwing getter would
// matter never gets serialized.
internal sealed class QueryCachingBehavior<TRequest, TResponse>(ICacheService cacheService)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, ICachedQuery
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        TResponse? cached = await cacheService.GetAsync<TResponse>(request.CacheKey, cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        TResponse response = await next(cancellationToken);

        if (response.IsSuccess)
        {
            await cacheService.SetAsync(request.CacheKey, response, request.Expiration, cancellationToken);
        }

        return response;
    }
}
