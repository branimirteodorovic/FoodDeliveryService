using System.Collections.Concurrent;
using System.Reflection;

namespace FoodDeliveryService.Common.Infrastructure.Correlation;

/// <summary>
/// The asynchronous counterpart of <c>BusinessIdRouteValues</c>. A job has no matched route to read
/// a business id off, but the message itself carries one — <c>OrderPlacedDomainEvent.OrderId</c>,
/// <c>DeliveryAssignedIntegrationEvent.DeliveryId</c> — and putting it on the dispatch's log lines
/// is what makes <c>OrderId = '…'</c> in Seq return the placement request <i>and</i> the outbox
/// dispatch <i>and</i> the consuming handler, which is what "search for all logs related to one
/// order" actually means.
/// </summary>
internal static class MessageBusinessIds
{
    private const string IdSuffix = "Id";

    /// <summary>
    /// Reflection over a message type happens once per process; the dispatch loop then only reads
    /// the cached properties. Events are small records, so the per-type list is a handful of entries.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> IdProperties = new();

    /// <summary>
    /// Every <c>Guid</c> property whose name ends in <c>Id</c>, as (property name, value) pairs.
    /// The event's own <c>Id</c> is excluded: it identifies the message, not the aggregate, and
    /// logging it under a name that generic would collide across every module.
    /// </summary>
    public static List<KeyValuePair<string, string>> Extract(object message)
    {
        List<KeyValuePair<string, string>> businessIds = [];

        foreach (PropertyInfo property in IdProperties.GetOrAdd(message.GetType(), Discover))
        {
            if (property.GetValue(message) is Guid value && value != Guid.Empty)
            {
                businessIds.Add(new KeyValuePair<string, string>(property.Name, value.ToString()));
            }
        }

        return businessIds;
    }

    private static PropertyInfo[] Discover(Type messageType) =>
        [.. messageType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property =>
                property.CanRead &&
                property.GetIndexParameters().Length == 0 &&
                (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?)) &&
                property.Name.Length > IdSuffix.Length &&
                property.Name.EndsWith(IdSuffix, StringComparison.Ordinal))];
}
