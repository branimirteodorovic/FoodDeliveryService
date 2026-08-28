using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Agents.UpsertSupportAgent;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Support.Presentation.Agents;

/// <summary>
/// Maintains the local SupportAgentReplica (dispatched by ProcessInboxJob, idempotent via the
/// inbox). Every registration on the platform reaches this handler, so all but the two staff roles
/// are skipped — this table is the set of people a ticket can be assigned to, not a second copy of
/// the user directory.
/// </summary>
internal sealed class UserRegisteredIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    // Role names as seeded by the Users service (Users.Domain Role) — carried in the event's role
    // snapshot; the Users domain itself is never referenced (hard rule #4). Administrators are
    // replicated alongside agents because they can do everything an agent can, assignment included,
    // and a ticket assigned to an administrator must render a name like any other.
    private static readonly string[] AssignableRoles = ["SupportAgent", "Administrator"];

    public override async Task Handle(
        UserRegisteredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (!integrationEvent.Roles.Any(AssignableRoles.Contains))
        {
            return;
        }

        Result result = await sender.Send(
            new UpsertSupportAgentCommand(
                integrationEvent.UserId,
                integrationEvent.Email,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpsertSupportAgentCommand),
                result.Error);
        }
    }
}
