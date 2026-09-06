using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Tickets.ChangeTicketStatus;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Tickets;

/// <summary>
/// One endpoint for every agent-driven status move, rather than five verb endpoints. The aggregate
/// already owns the table of which moves are legal from which state; five endpoints would each
/// carry a copy of part of it.
///
/// An illegal move is a 400 with problem details, not a 500 — the aggregate returns a Result.
/// </summary>
internal sealed class ChangeTicketStatus : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("support/tickets/{id:guid}/status", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(
                new ChangeTicketStatusCommand(id, request.Status, request.Reason),
                cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageTickets)
        .WithTags(Tags.Tickets)
        .WithSummary("Change a ticket's status")
        .WithDescription(
            "Drives the ticket through its lifecycle. The aggregate decides which transitions are " +
            "legal; an illegal one is a 409, not a silently ignored write.")
        .Produces(StatusCodes.Status204NoContent);
    }

    internal sealed class Request
    {
        public string Status { get; init; } = string.Empty;

        /// <summary>The resolution note for Resolved, the reason for Escalated. Required by both.</summary>
        public string? Reason { get; init; }
    }
}
