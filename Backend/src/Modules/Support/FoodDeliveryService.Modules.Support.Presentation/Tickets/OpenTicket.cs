using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Tickets.OpenTicket;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Tickets;

/// <summary>
/// A customer opens a support ticket. The ticket belongs to the authenticated caller — the request
/// body has no customer id, only the optional <c>OnBehalfOfCustomerId</c> an agent uses to file a
/// ticket from a phone call, which the handler gates on <c>support-tickets:manage</c>.
/// </summary>
internal sealed class OpenTicket : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("support/tickets", async (
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<Guid> result = await sender.Send(
                new OpenTicketCommand(
                    request.OnBehalfOfCustomerId,
                    request.OrderId,
                    request.Subject,
                    request.Category),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.OpenTicket)
        .WithTags(Tags.Tickets);
    }

    internal sealed class Request
    {
        /// <summary>Agent-only. Absent on every customer-opened ticket.</summary>
        public Guid? OnBehalfOfCustomerId { get; init; }

        /// <summary>Optional — not every ticket is about an order.</summary>
        public Guid? OrderId { get; init; }

        public string Subject { get; init; } = string.Empty;

        public string Category { get; init; } = nameof(Domain.Tickets.TicketCategory.Other);
    }
}
