using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Common.Application.Caching;

/// <summary>
/// Opts a query into <c>QueryCachingBehavior</c> declaratively: the query itself supplies the
/// cache key and TTL, so a handler stays a plain single-Dapper-read implementation with no
/// caching code inside it.
/// </summary>
public interface ICachedQuery
{
    string CacheKey { get; }

    TimeSpan? Expiration { get; }
}

public interface ICachedQuery<TResponse> : IQuery<TResponse>, ICachedQuery;
