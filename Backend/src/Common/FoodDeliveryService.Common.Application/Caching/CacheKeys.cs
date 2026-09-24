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
    /// The caller's permission set, as <c>IPermissionService</c> caches it for five minutes.
    /// <para>
    /// This key lives in <b>Common</b>, not in a module's convention class, because unlike every
    /// other cached surface it is <b>not owned by one service</b>. Seven <c>PermissionService</c>
    /// implementations write it and they all write the <em>same entry</em>: the key carries no
    /// service qualifier and <c>AddStackExchangeRedisCache</c> is registered with no
    /// <c>InstanceName</c>, so nothing prefixes it per process, and there is one Redis instance per
    /// environment. One <c>RemoveAsync</c> from anywhere therefore evicts the caller's permissions
    /// for the whole platform at once — which is what lets a role change in Users take effect
    /// everywhere without an integration event or a consumer in six services.
    /// </para>
    /// <para>
    /// It is keyed by the <b>identity-provider id</b> (the token's <c>sub</c>), not the module-side
    /// <c>users.id</c> — that is what the authorization path has in hand before it knows who the
    /// caller is. A writer that used the wrong id would miss silently, which is why the key is built
    /// here rather than at each of the eight call sites. See <c>docs/caching.md</c> §2.
    /// </para>
    /// </summary>
    public static string UserPermissions(string identityId) => Create("user_permissions", identityId);

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
