using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Users.Application.Users.GetCurrentUser;
using FoodDeliveryService.Modules.Users.Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Users.Presentation.Users;

/// <summary>
/// "Who am I?" — the self-scoped read a client makes once after sign-in. Same shape as
/// <c>delivery/drivers/me/offers</c>: the subject is the token, never a route or query parameter.
/// <para>
/// It is gated on <c>users:read</c>, which every role holds, so the permission is not what decides
/// whose record comes back — the handler is. The permission is here because an authenticated caller
/// is not automatically a caller this module knows about, and <c>users:read</c> is the code that
/// says they are.
/// </para>
/// <para>
/// Unlike the other modules' endpoints this names the permission through
/// <see cref="Permission.GetUser"/> rather than a module-local <c>Permissions</c> constant. Those
/// constants exist because a module may not reference Users' Domain; Users <em>is</em> that domain,
/// and a copy of the string here would be the one copy that can drift from the catalogue it is
/// copied from.
/// </para>
/// </summary>
internal sealed class GetCurrentUser : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("users/me", async (ISender sender, CancellationToken cancellationToken) =>
        {
            Result<CurrentUserResponse> result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permission.GetUser.Code)
        .WithTags(Tags.Users)
        .WithSummary("Get the calling user's profile and roles")
        .WithDescription(
            "The signed-in user's own record: their module-side id, name, email and role names. " +
            "Resolved from the token, so there is no parameter with which to ask for anyone else. " +
            "Two things here are available nowhere else on the client. The roles are not in the " +
            "access token at all - the API resource declares no user claims, so they never leave " +
            "this module unless asked for. And userId is the MODULE-side id (users.id), which is " +
            "what OrderResponse.customerId and the SignalR user:{id} group are keyed by - the " +
            "token's sub claim is the identity-provider id and matches neither. Intended for " +
            "driving navigation and what a UI renders; it is not an authorization check, which " +
            "every service still performs per request.")
        .Produces<CurrentUserResponse>();
    }
}
