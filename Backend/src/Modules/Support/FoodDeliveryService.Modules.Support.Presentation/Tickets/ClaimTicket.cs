using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Tickets.ClaimTicket;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Tickets;

/// <summary>
/// An agent takes a ticket out of the queue. No request body at all: the agent is the authenticated
/// caller, and the ticket is the route. Losing the race for the claim returns a clean failure the
/// client can retry — the ticket is still queued, so nothing is stranded.
/// </summary>
internal sealed class ClaimTicket : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("support/tickets/{id:guid}/claim", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new ClaimTicketCommand(id), cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.AssignTickets)
        .WithTags(Tags.Tickets);
    }
}
