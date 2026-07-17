using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Drivers.SyncDriverFromUserProfile;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Drivers;

/// <summary>
/// Keeps the Driver's name snapshot in sync. The command no-ops for users that are not drivers
/// (customers, managers, admins), so every profile update can be consumed safely.
/// </summary>
internal sealed class UserProfileUpdatedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserProfileUpdatedIntegrationEvent>
{
    public override async Task Handle(
        UserProfileUpdatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new SyncDriverFromUserProfileCommand(
                integrationEvent.UserId,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SyncDriverFromUserProfileCommand),
                result.Error);
        }
    }
}
