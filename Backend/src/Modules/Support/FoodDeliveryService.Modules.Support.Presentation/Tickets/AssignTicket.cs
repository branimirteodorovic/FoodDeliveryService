using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Tickets.AssignTicket;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Tickets;

/// <summary>
/// Routing a ticket to a named agent. The route policy only asks for <c>support-tickets:assign</c>,
/// which agents and administrators both hold; naming an agent other than yourself additionally
/// requires the administrator bypass, and that check lives in the handler because it depends on the
/// body. An agent naming themselves is allowed — it is the assign-side equivalent of a claim, and
/// unlike a claim it can take over a ticket that is already in progress.
/// </summary>
internal sealed class AssignTicket : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("support/tickets/{id:guid}/assign", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(
                new AssignTicketCommand(id, request.AgentId, request.Reason),
                cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.AssignTickets)
        .WithTags(Tags.Tickets);
    }

    internal sealed class Request
    {
        /// <summary>
        /// The agent to put on the ticket. This is the one id in the body that may name somebody
        /// else, which is exactly why the handler gates it — the acting user is never taken from
        /// here, only from the token.
        /// </summary>
        public Guid AgentId { get; init; }

        /// <summary>Optional; recorded on the audit entry when supplied.</summary>
        public string? Reason { get; init; }
    }
}
