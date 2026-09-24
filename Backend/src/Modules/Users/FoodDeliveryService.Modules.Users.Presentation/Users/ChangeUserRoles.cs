using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Users.Application.Users.ChangeUserRoles;
using FoodDeliveryService.Modules.Users.Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Users.Presentation.Users;

/// <summary>
/// Replaces a user's role set — the only way a role changes after the account exists, and the only
/// endpoint on the platform whose effect is felt by every other service.
/// <para>
/// <c>PUT</c> rather than <c>POST</c>/<c>PATCH</c> because the body <em>is</em> the resulting set:
/// sending the same body twice leaves the same state, and there is no grant/revoke ordering to get
/// wrong. Like <see cref="GetCurrentUser"/> this names its permission through
/// <see cref="Permission.ManageUserRoles"/> rather than a module-local constant — Users owns that
/// catalogue, so a copy of the string here would be the one copy able to drift from it.
/// </para>
/// </summary>
internal sealed class ChangeUserRoles : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("users/{userId:guid}/roles", async (
            Guid userId,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new ChangeUserRolesCommand(userId, request.Roles ?? []);

            Result result = await sender.Send(command, cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permission.ManageUserRoles.Code)
        .WithTags(Tags.Users)
        .WithSummary("Replace a user's roles")
        .WithDescription(
            "Sets the user's roles to exactly the list sent, which is what every service's "
            + "authorization is resolved from. Names are the seeded role names - Customer, "
            + "RestaurantManager, DeliveryDriver, SupportAgent. Administrator is not among them: it "
            + "can neither be granted here nor stripped, and a request against an administrator's "
            + "account is refused, because the only Administrator on the platform is the one seeded "
            + "from configuration. At least one role is required - an account with none resolves as "
            + "unknown rather than as restricted. "
            + "The change is immediate: the shared permission cache entry is evicted in the same "
            + "call, so the next request this user makes to ANY service is authorized against the "
            + "new set rather than waiting out the five-minute TTL. Their access token is not "
            + "affected - it carries no roles and no permissions - so nothing needs reissuing. "
            + "Services that keep a role-filtered copy of the user directory are back-filled "
            + "asynchronously over the bus, so a freshly granted role can take a moment to become "
            + "assignable work; a revoked one stops authorizing immediately either way.")
        .Produces(StatusCodes.Status204NoContent);
    }

    internal sealed class Request
    {
        /// <summary>The roles the user should hold after the call — the whole set, not a delta.</summary>
        public IReadOnlyCollection<string>? Roles { get; init; }
    }
}
