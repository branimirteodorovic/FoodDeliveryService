using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Users.IntegrationEvents;

/// <summary>
/// An administrator replaced a user's roles. Carries the same identity snapshot as
/// <see cref="UserRegisteredIntegrationEvent"/> and for the same reason: the services that keep a
/// <b>role-filtered</b> replica of the user directory — Orders (Customer), Restaurants
/// (RestaurantManager), Support (SupportAgent) — build those replicas from the registration event
/// and skip everyone who did not hold the role at the time. Without this event a user granted the
/// role afterwards would hold its permissions and still be absent from the table that makes them
/// usable: no Orders customer row means <c>PlaceOrder</c> refuses them, and no Support agent row
/// means no ticket can be assigned to them.
/// <para>
/// The <b>revoke</b> direction deliberately does not remove a replica row. A row there is a name,
/// not an authorization — every permission is re-resolved per request from Users — and deleting one
/// would orphan the tickets and orders that already point at it. A revoked agent stops being able
/// to act immediately; they keep rendering as a name on the work they already did.
/// </para>
/// </summary>
public sealed class UserRolesChangedIntegrationEvent : IntegrationEvent
{
    public UserRolesChangedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid userId,
        string email,
        string firstName,
        string lastName,
        IReadOnlyCollection<string> roles)
        : base(id, occurredOnUtc)
    {
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        Roles = roles;
    }

    public Guid UserId { get; init; }

    public string Email { get; init; }

    public string FirstName { get; init; }

    public string LastName { get; init; }

    /// <summary>The user's roles <b>after</b> the change — the whole set, not a delta.</summary>
    public IReadOnlyCollection<string> Roles { get; init; }
}
