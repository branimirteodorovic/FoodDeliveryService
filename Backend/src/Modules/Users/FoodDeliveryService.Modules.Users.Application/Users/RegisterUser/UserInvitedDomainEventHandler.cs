using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.IntegrationEvents;

namespace FoodDeliveryService.Modules.Users.Application.Users.RegisterUser;

/// <summary>
/// Publishes <see cref="UserInvitedIntegrationEvent"/> when an invited account is provisioned, so
/// Notifications can email the activation link. All data (including the one-time token) is carried
/// on the domain event, so no callback query is needed.
/// </summary>
internal sealed class UserInvitedDomainEventHandler(IEventBus bus)
    : DomainEventHandler<UserInvitedDomainEvent>
{
    public override async Task Handle(
        UserInvitedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await bus.PublishAsync(
            new UserInvitedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.UserId,
                domainEvent.Email,
                domainEvent.FirstName,
                domainEvent.LastName,
                domainEvent.ActivationToken,
                domainEvent.ExpiresOnUtc),
            cancellationToken);
    }
}
