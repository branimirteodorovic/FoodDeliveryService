using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Managers.UpsertManager;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Managers;

/// <summary>
/// Maintains the local RestaurantManager replica (dispatched by ProcessInboxJob, idempotent via the
/// inbox). Fires for every registration — customers included — so non-manager users are skipped;
/// only RestaurantManager accounts are replicated here.
/// </summary>
internal sealed class UserRegisteredIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    // Role name as seeded by the Users service (Users.Domain Role.RestaurantManager) — carried in
    // the event's role snapshot; the Users domain itself is not referenced (hard rule #4).
    private const string RestaurantManagerRole = "RestaurantManager";

    public override async Task Handle(
        UserRegisteredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (!integrationEvent.Roles.Contains(RestaurantManagerRole))
        {
            return;
        }

        Result result = await sender.Send(
            new UpsertRestaurantManagerCommand(
                integrationEvent.UserId,
                integrationEvent.Email,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpsertRestaurantManagerCommand),
                result.Error);
        }
    }
}
