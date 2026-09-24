using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Users.GetUser;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Users.Application.Users.ChangeUserRoles;

/// <summary>
/// Publishes the role change outward. Reads the user back for the identity snapshot exactly as
/// <c>UserRegisteredDomainEventHandler</c> does — the domain event carries the roles, and the
/// integration event has to carry a full snapshot (Hard Rule #9) so a consumer building a replica
/// for a role it has never seen before has the name and email to build it with.
/// <para>
/// The read-back is safe here for the reason it was not in Restaurants' menu handlers: nothing in
/// this path reads a cached key that this command evicts. <c>GetUserQuery</c> is uncached, and the
/// one key the handler's command does evict — <c>user_permissions</c> — is written by the
/// authorization path, not by this snapshot.
/// </para>
/// </summary>
internal sealed class UserRolesChangedDomainEventHandler(ISender sender, IEventBus bus)
    : DomainEventHandler<UserRolesChangedDomainEvent>
{
    public override async Task Handle(
        UserRolesChangedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        Result<UserResponse> result = await sender.Send(
            new GetUserQuery(domainEvent.UserId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(nameof(GetUserQuery), result.Error);
        }

        await bus.PublishAsync(
            new UserRolesChangedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                result.Value.Id,
                result.Value.Email,
                result.Value.FirstName,
                result.Value.LastName,
                domainEvent.Roles),
            cancellationToken);
    }
}
