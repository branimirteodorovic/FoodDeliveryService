namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// Centralises the SignalR group naming so the hub and the fan-out consumers agree on exactly one
/// spelling per audience — and so the derivation is unit-testable without a running hub. A client is
/// only ever placed in groups derived from its own JWT claims (never from client-supplied ids):
/// a customer lands in <see cref="User"/>, a restaurant manager in <see cref="Restaurant"/>, a
/// support agent in <see cref="Support"/>.
/// </summary>
public static class GroupNames
{
    /// <summary>The per-customer group. Order-status and driver-location frames for a customer's
    /// own orders are broadcast here; the id is the module-side user id (the JWT's <c>sub</c>).</summary>
    public static string User(Guid userId) => $"user:{userId}";

    /// <summary>The per-restaurant dashboard group (Milestone D). The id is the manager's
    /// restaurant, resolved from a replica — never trusted from the caller.</summary>
    public static string Restaurant(Guid restaurantId) => $"restaurant:{restaurantId}";

    /// <summary>The single global support-dashboard group (Milestone D).</summary>
    public const string Support = "support";
}
