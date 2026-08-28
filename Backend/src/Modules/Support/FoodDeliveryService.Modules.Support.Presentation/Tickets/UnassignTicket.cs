using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Tickets.UnassignTicket;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Tickets;

/// <summary>
/// Puts a ticket back in the queue. The reason is required — an unexplained hand-back is the one
/// audit entry that would tell a reviewer nothing — and it is the aggregate, not this endpoint, that
/// refuses an empty one.
/// </summary>
internal sealed class UnassignTicket : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("support/tickets/{id:guid}/unassign", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(
                new UnassignTicketCommand(id, request.Reason),
                cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.AssignTickets)
        .WithTags(Tags.Tickets);
    }

    internal sealed class Request
    {
        public string Reason { get; init; } = string.Empty;
    }
}
