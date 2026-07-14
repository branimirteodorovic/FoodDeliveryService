using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Users.Domain.Users;

public sealed class UserRegisteredDomainEvent(Guid userId, IReadOnlyCollection<string> roles) : DomainEvent
{
    public Guid UserId { get; init; } = userId;

    // Identity/role snapshot carried to Orders/Restaurants via UserRegisteredIntegrationEvent so they
    // never have to call back into Users for it. Normalized to a plain array: the event is serialized
    // into the outbox and round-tripped by Newtonsoft (TypeNameHandling), which cannot reconstruct the
    // compiler-synthesized read-only list types that C# collection expressions ([role.Name]) produce.
    public IReadOnlyCollection<string> Roles { get; init; } = roles.ToArray();
}
