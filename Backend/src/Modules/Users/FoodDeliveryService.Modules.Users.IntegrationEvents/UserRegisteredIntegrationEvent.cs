using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Users.IntegrationEvents;

public sealed class UserRegisteredIntegrationEvent : IntegrationEvent
{
    public UserRegisteredIntegrationEvent(
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

    // Identity/role snapshot only — no business fields (this contract is shared with Orders/Restaurants).
    public IReadOnlyCollection<string> Roles { get; init; }
}
