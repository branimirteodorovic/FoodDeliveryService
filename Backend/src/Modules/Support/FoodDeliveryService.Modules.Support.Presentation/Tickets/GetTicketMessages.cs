using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketMessages;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Tickets;

/// <summary>
/// One ticket's conversation, oldest first. Same permission for both audiences — a customer sees
/// their own thread, an agent sees any — and the difference between the two is the internal notes,
/// which the query leaves in Postgres for a customer caller rather than filtering them out on the
/// way back.
/// </summary>
internal sealed class GetTicketMessages : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("support/tickets/{id:guid}/messages", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyCollection<TicketMessageResponse>> result = await sender.Send(
                new GetTicketMessagesQuery(id),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetTickets)
        .WithTags(Tags.Tickets);
    }
}
