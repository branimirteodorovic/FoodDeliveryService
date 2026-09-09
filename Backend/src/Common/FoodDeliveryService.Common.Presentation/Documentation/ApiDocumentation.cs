namespace FoodDeliveryService.Common.Presentation.Documentation;

/// <summary>
/// The documented services, in one place — Feature 3.7 Milestone G.
/// <para>
/// Naming the services from <c>Common.Presentation</c> looks at first like a layering violation, and
/// is not: these are strings, not references, and the same argument already put
/// <see cref="RateLimiting.RateLimitRoutePolicy"/>'s route table here. The alternative was a
/// descriptor built inline in every <c>Program.cs</c>, which is where the seven copies of
/// <c>SwaggerExtensions</c> came from — and it would also put the titles somewhere
/// <c>OpenApiDocumentTests</c> cannot read them, so the completeness test would be asserting against
/// a second transcription of the same strings.
/// </para>
/// <para>
/// A host passes its own descriptor and nothing else:
/// <c>builder.Services.AddApiDocumentation(builder.Configuration, ApiDocumentation.Orders)</c>.
/// </para>
/// </summary>
public static class ApiDocumentation
{
    /// <summary>
    /// The Orders service — <c>orders/**</c> at the Gateway, hosted by
    /// <c>FoodDeliveryService.Orders.Api</c>.
    /// </summary>
    public static ApiDocumentationDescriptor Orders { get; } = new(
        Slug: "orders",
        Title: "FoodDeliveryService — Orders API",
        Description:
            "The order lifecycle: a customer places an order, the restaurant accepts it, prepares " +
            "it and marks it ready, and it is either cancelled or carried to Delivered by the " +
            "delivery service. Orders owns order state and keeps local replicas of users, " +
            "restaurants and menu prices, so placing an order never calls another service.");

    /// <summary>
    /// The Restaurants service — <c>restaurants/**</c> at the Gateway, hosted by
    /// <c>FoodDeliveryService.Restaurants.Api</c>.
    /// </summary>
    public static ApiDocumentationDescriptor Restaurants { get; } = new(
        Slug: "restaurants",
        Title: "FoodDeliveryService — Restaurants API",
        Description:
            "Restaurants and their menus: onboarding, profile and address, menu categories and " +
            "items, and per-item availability. This is the storefront's read surface, and the " +
            "source Orders replicates prices from.");

    /// <summary>
    /// The Users service — <c>users/**</c> at the Gateway, hosted by
    /// <c>FoodDeliveryService.Users.Api</c>.
    /// </summary>
    public static ApiDocumentationDescriptor Users { get; } = new(
        Slug: "users",
        Title: "FoodDeliveryService — Users API",
        Description:
            "Registration, invitation acceptance, profiles, roles and permissions. It is the " +
            "platform's authorization source: every other service resolves a caller's permissions " +
            "from here over the message bus rather than reading this database.");

    /// <summary>
    /// The Delivery service — <c>delivery/**</c> at the Gateway, hosted by
    /// <c>FoodDeliveryService.Delivery.Api</c>.
    /// </summary>
    public static ApiDocumentationDescriptor Delivery { get; } = new(
        Slug: "delivery",
        Title: "FoodDeliveryService — Delivery API",
        Description:
            "Driver onboarding, availability and live position, and the delivery half of an " +
            "order's life: an offer goes to the nearest available driver, is accepted or rejected " +
            "inside a fixed window, and is then picked up and delivered — which is what closes " +
            "the order.");

    /// <summary>
    /// The Support service — <c>support/**</c> at the Gateway, hosted by
    /// <c>FoodDeliveryService.Support.Api</c>.
    /// </summary>
    public static ApiDocumentationDescriptor Support { get; } = new(
        Slug: "support",
        Title: "FoodDeliveryService — Support API",
        Description:
            "Support tickets and their lifecycle: agent assignment, an append-only audit log " +
            "written in the same transaction as the change it records, the agent-to-customer " +
            "message thread, and refund requests — which one agent asks for and a different " +
            "administrator decides. No money moves: the platform has no payment processing.");

    /// <summary>
    /// The RealTime service — <c>hubs/**</c> at the Gateway, hosted by
    /// <c>FoodDeliveryService.RealTime.Api</c>.
    /// </summary>
    public static ApiDocumentationDescriptor RealTime { get; } = new(
        Slug: "realtime",
        Title: "FoodDeliveryService — Real-Time API",
        Description:
            "Live order and delivery tracking over SignalR. This service's real surface is the " +
            "`hubs/tracking` hub rather than an HTTP route, and the hub derives a caller's groups " +
            "from their permission claims after the handshake — which is why the negotiate " +
            "endpoint authorizes any authenticated principal instead of naming a permission.");

    /// <summary>
    /// The Payments service — <c>payments/**</c> at the Gateway, hosted by
    /// <c>FoodDeliveryService.Payments.Api</c>.
    /// </summary>
    public static ApiDocumentationDescriptor Payments { get; } = new(
        Slug: "payments",
        Title: "FoodDeliveryService — Payments API",
        Description:
            "Card payments through Stripe: a customer saves a card, an order authorizes against " +
            "it when it is placed, the authorization is captured when the restaurant accepts and " +
            "released when the order is rejected or cancelled, and an approved refund request is " +
            "settled back to the card. The service stores Stripe identifiers, card brand and last " +
            "four digits only — no card number ever reaches this platform. This document has no " +
            "operations yet: the service exists, its API surface does not.");

    /// <summary>
    /// The Notifications service — <c>notifications/**</c> at the Gateway, hosted by
    /// <c>FoodDeliveryService.Notifications.Api</c>.
    /// </summary>
    public static ApiDocumentationDescriptor Notifications { get; } = new(
        Slug: "notifications",
        Title: "FoodDeliveryService — Notifications API",
        Description:
            "Notifications reacts to integration events and sends email; it deliberately exposes " +
            "no HTTP endpoints, so this document has no operations. It is published anyway, " +
            "because a service with an empty API surface and a service whose documentation was " +
            "forgotten look identical otherwise.");

    /// <summary>
    /// Every documented service. Enumerated by <c>OpenApiDocumentTests</c>, and by
    /// <c>docs/api-documentation.md</c>'s URL table when it is regenerated by hand — a service added
    /// here without a Gateway <c>docs/{slug}/**</c> route fails <c>GatewayRouteTests</c>.
    /// </summary>
    public static IReadOnlyList<ApiDocumentationDescriptor> All { get; } =
    [
        Orders,
        Restaurants,
        Users,
        Delivery,
        Support,
        RealTime,
        Notifications,
        Payments
    ];
}
