namespace FoodDeliveryService.Common.Application.Caching;

/// <summary>
/// Builds cache keys from a single namespaced convention so they're constructed in one place
/// instead of string-concatenated at each call site.
/// </summary>
public static class CacheKeys
{
    public static string Create(string area, object id) => $"{area}:{id}";

    public static string Create(string area, string entity, object id) => $"{area}:{entity}:{id}";
}
