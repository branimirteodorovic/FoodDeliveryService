using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace FoodDeliveryService.Common.Presentation.Correlation;

/// <summary>
/// Turns the id parameters of the matched route into log-property names, so Seq is searchable by
/// order — <c>OrderId = '…'</c> returns every line the platform wrote about that order, across the
/// request pipeline and every module that logged under the same scope.
/// <para>
/// The route is the only place a stable business id is available to a middleware: the request body
/// has not been read, and reading it here would buffer every payload in the system to enrich a log.
/// So this covers <c>GET orders/{id}</c> and its siblings, and deliberately not <c>POST orders</c>,
/// whose id does not exist until the handler creates it.
/// </para>
/// </summary>
internal static class BusinessIdRouteValues
{
    /// <summary>
    /// A GUID is 36 characters; the cap only exists to keep an unconstrained route parameter — a
    /// caller-controlled string — from putting an arbitrarily large value on every log line.
    /// </summary>
    private const int MaxValueLength = 64;

    private const string IdSuffix = "id";

    /// <summary>
    /// Every route value whose name ends in <c>id</c>, as (property name, value) pairs. A parameter
    /// with its own name (<c>restaurantId</c>) becomes <c>RestaurantId</c>; the bare <c>id</c> the
    /// endpoints use for their own aggregate (<c>orders/{id}</c>, <c>delivery/drivers/{id}</c>) is
    /// qualified with the resource segment it follows, giving <c>OrderId</c> and <c>DriverId</c>
    /// rather than seven different modules all logging <c>Id</c>.
    /// </summary>
    public static List<KeyValuePair<string, string>> Extract(HttpContext context)
    {
        List<KeyValuePair<string, string>> businessIds = [];

        foreach (KeyValuePair<string, object?> routeValue in context.Request.RouteValues)
        {
            if (!routeValue.Key.EndsWith(IdSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? value = routeValue.Value?.ToString();

            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxValueLength)
            {
                continue;
            }

            businessIds.Add(new KeyValuePair<string, string>(PropertyName(context, routeValue.Key), value));
        }

        return businessIds;
    }

    private static string PropertyName(HttpContext context, string parameterName)
    {
        if (!string.Equals(parameterName, IdSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return ToPascalCase(parameterName);
        }

        string? resource = ResourcePrecedingParameter(context, parameterName);

        return resource is null ? ToPascalCase(parameterName) : $"{ToPascalCase(Singular(resource))}Id";
    }

    /// <summary>
    /// The last literal segment before the parameter in the route <i>pattern</i> — the resource the
    /// id belongs to. Read from the pattern rather than the request path because the pattern is what
    /// the endpoint declared: <c>orders/{id}/cancel</c> answers "orders" whichever segment the value
    /// happens to sit in.
    /// </summary>
    private static string? ResourcePrecedingParameter(HttpContext context, string parameterName)
    {
        if (context.GetEndpoint() is not RouteEndpoint endpoint)
        {
            return null;
        }

        string? lastLiteral = null;

        foreach (RoutePatternPathSegment segment in endpoint.RoutePattern.PathSegments)
        {
            foreach (RoutePatternPart part in segment.Parts)
            {
                switch (part)
                {
                    case RoutePatternLiteralPart literal:
                        lastLiteral = literal.Content;
                        break;

                    case RoutePatternParameterPart parameter
                        when string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase):
                        return lastLiteral;

                    default:
                        break;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Enough singularisation for the resource names this platform actually routes on — "orders",
    /// "drivers", "deliveries", "restaurants". It is a log-property name, not an identifier: a
    /// resource this misses is still logged, just under a slightly clumsy name.
    /// </summary>
    private static string Singular(string resource)
    {
        if (resource.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(resource.AsSpan(0, resource.Length - 3), "y");
        }

        return resource.EndsWith('s') ? resource[..^1] : resource;
    }

    /// <summary>
    /// <c>restaurantId</c> → <c>RestaurantId</c>, <c>menu-item</c> → <c>MenuItem</c>: Serilog
    /// property names are PascalCase everywhere else in the platform, and Seq's query syntax has no
    /// way to quote a name containing a dash.
    /// </summary>
    private static string ToPascalCase(string value)
    {
        string[] words = value.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);

        return string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
