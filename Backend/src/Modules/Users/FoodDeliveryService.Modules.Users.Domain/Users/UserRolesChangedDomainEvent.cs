using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Users.Domain.Users;

/// <summary>
/// An administrator replaced a user's role set. Raised only on a real change — see
/// <see cref="User.ChangeRoles"/>, which no-ops on an unchanged set the way
/// <see cref="User.Update"/> does.
/// </summary>
public sealed class UserRolesChangedDomainEvent(Guid userId, IReadOnlyCollection<string> roles) : DomainEvent
{
    public Guid UserId { get; init; } = userId;

    // Normalized to a plain array for the same reason UserRegisteredDomainEvent does it: the event
    // is serialized into the outbox and round-tripped by Newtonsoft (TypeNameHandling), which cannot
    // reconstruct the compiler-synthesized read-only list types a collection expression produces.
    public IReadOnlyCollection<string> Roles { get; init; } = roles.ToArray();
}
