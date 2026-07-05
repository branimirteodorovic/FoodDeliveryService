using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Users.SendUserInvitationEmail;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Presentation.Users;

/// <summary>
/// Reacts to an account being provisioned by invitation (published by Users) and sends the
/// invitation email carrying the activation link. Dispatched by ProcessInboxJob (idempotent).
/// </summary>
internal sealed class UserInvitedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserInvitedIntegrationEvent>
{
    public override async Task Handle(
        UserInvitedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new SendUserInvitationEmailCommand(
                integrationEvent.Email,
                integrationEvent.FirstName,
                integrationEvent.ActivationToken,
                integrationEvent.ExpiresOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SendUserInvitationEmailCommand),
                result.Error);
        }
    }
}
