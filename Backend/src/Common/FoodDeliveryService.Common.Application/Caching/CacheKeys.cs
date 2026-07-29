namespace FoodDeliveryService.Common.Application.Caching;

/// <summary>
/// Builds cache keys from a single namespaced convention so they're constructed in one place
/// instead of string-concatenated at each call site.
/// </summary>
public static class CacheKeys
{
    public static string Create(string area, object id) => $"{area}:{id}";

    public static string Create(string area, string entity, object id) => $"{area}:{entity}:{id}";

    /// <summary>
    /// The inverse of <see cref="Create(string, object)"/>: drops the trailing id segment, so
    /// <c>restaurants:menu:{guid}</c> becomes <c>restaurants:menu</c> and
    /// <c>user_permissions:{identityId}</c> becomes <c>user_permissions</c>. Both <c>Create</c>
    /// overloads put the id last, which is what makes dropping the last segment safe.
    /// <para>
    /// Used as the metric tag for cache hits/misses: hit rate stays readable per cached surface
    /// instead of exploding into one time series per restaurant or user.
    /// </para>
    /// </summary>
    public static string Prefix(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        int lastSeparator = key.LastIndexOf(':');

        // A key with no separator has no id to drop — it is already the prefix.
        return lastSeparator <= 0 ? key : key[..lastSeparator];
    }
}
