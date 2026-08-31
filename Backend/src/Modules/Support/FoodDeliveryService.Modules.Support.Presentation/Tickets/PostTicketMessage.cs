using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Tickets.PostTicketMessage;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Tickets;

/// <summary>
/// Posts a message to a ticket's thread — a customer's reply on their own ticket, or an agent's.
/// <para>
/// Gated on <c>support-tickets:read</c>, which both customers and agents hold, because it is the one
/// write in this module a customer performs on a ticket somebody else may be managing. Who may write
/// <em>what</em> is decided further in: the handler refuses an internal note from a caller without
/// <c>support-tickets:manage</c>, and the aggregate refuses one from a customer regardless of how it
/// got there.
/// </para>
/// </summary>
internal sealed class PostTicketMessage : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("support/tickets/{id:guid}/messages", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<Guid> result = await sender.Send(
                new PostTicketMessageCommand(id, request.Body, request.Visibility),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetTickets)
        .WithTags(Tags.Tickets);
    }

    internal sealed class Request
    {
        public string Body { get; init; } = string.Empty;

        /// <summary>
        /// Defaults to the customer-visible reply. An <c>InternalNote</c> needs
        /// <c>support-tickets:manage</c> — omitting the field can therefore never publish a note by
        /// accident, only the other way round.
        /// </summary>
        public string Visibility { get; init; } =
            nameof(Domain.Tickets.TicketMessageVisibility.CustomerVisible);
    }
}
