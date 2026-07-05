using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Users.IntegrationEvents;

/// <summary>
/// Published when an account is provisioned by invitation (no password). Notifications consumes it
/// to build the activation link and email the invitee. Carries the one-time activation token only —
/// never a password.
/// </summary>
public sealed class UserInvitedIntegrationEvent : IntegrationEvent
{
    public UserInvitedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid userId,
        string email,
        string firstName,
        string lastName,
        string activationToken,
        DateTime expiresOnUtc)
        : base(id, occurredOnUtc)
    {
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        ActivationToken = activationToken;
        ExpiresOnUtc = expiresOnUtc;
    }

    public Guid UserId { get; init; }

    public string Email { get; init; }

    public string FirstName { get; init; }

    public string LastName { get; init; }

    public string ActivationToken { get; init; }

    public DateTime ExpiresOnUtc { get; init; }
}
