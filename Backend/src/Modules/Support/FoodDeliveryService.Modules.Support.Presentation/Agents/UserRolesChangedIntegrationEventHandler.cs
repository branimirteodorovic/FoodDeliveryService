using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Agents.UpsertSupportAgent;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Support.Presentation.Agents;

/// <summary>
/// Back-fills the SupportAgentReplica when a role change grants SupportAgent to an account that did
/// not have it at registration — the most likely use of the role-change endpoint there is, since an
/// employee who already has a customer account is promoted rather than invited afresh.
/// <para>
/// This table is the set of people a ticket can be assigned to. Without this handler the promotion
/// would grant every <c>support-tickets:*</c> code and still leave the new agent unassignable, with
/// nothing anywhere reporting an error — the ticket would simply never list them. Same idempotent
/// upsert command as the registration handler, so a replay is free.
/// </para>
/// <para>
/// The revoke direction is deliberately not handled. The row is a name, not an authorization: a
/// revoked agent stops being able to open a ticket on the very next request (permissions are
/// resolved per request from Users, and the role change evicts the shared cache entry immediately),
/// while every ticket they already worked still has somebody to render.
/// </para>
/// </summary>
internal sealed class UserRolesChangedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserRolesChangedIntegrationEvent>
{
    // Role names as seeded by the Users service (Users.Domain Role) — carried in the event's role
    // snapshot; the Users domain itself is never referenced (hard rule #4). Administrator is kept in
    // the filter to mirror the registration handler exactly, though a role change can never produce
    // one: Administrator is outside Role.Assignable, and the aggregate refuses to touch an
    // administrator's roles at all.
    private static readonly string[] AssignableRoles = ["SupportAgent", "Administrator"];

    public override async Task Handle(
        UserRolesChangedIntegrationEvent integrationEvent,
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
