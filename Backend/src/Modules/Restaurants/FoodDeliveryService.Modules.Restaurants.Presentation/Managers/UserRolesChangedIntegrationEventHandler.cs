using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Managers.UpsertManager;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Managers;

/// <summary>
/// Back-fills the RestaurantManager replica when a role change grants the RestaurantManager role to
/// an account that did not have it at registration.
/// <para>
/// <see cref="UserRegisteredIntegrationEventHandler"/> is role-filtered and runs once, at
/// registration, so a manager promoted afterwards would otherwise hold <c>restaurants:update</c> and
/// be unknown to the table that ties a manager to a restaurant. Same idempotent upsert command, so a
/// replay is free; the revoke direction is deliberately not handled, since the row is the name
/// against the restaurants this person already manages.
/// </para>
/// </summary>
internal sealed class UserRolesChangedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserRolesChangedIntegrationEvent>
{
    // Role name as seeded by the Users service (Users.Domain Role.RestaurantManager) — carried in
    // the event's role snapshot; the Users domain itself is not referenced (hard rule #4).
    private const string RestaurantManagerRole = "RestaurantManager";

    public override async Task Handle(
        UserRolesChangedIntegrationEvent integrationEvent,
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
