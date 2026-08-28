using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Tickets.GetTickets;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Tickets;

/// <summary>
/// The agent queue (?status=Open&amp;unassigned=true), and a customer's own ticket list — the same
/// endpoint. Whose tickets come back is decided in the handler from the authenticated identity, not
/// from a query parameter, so there is nothing here a customer could set to widen their view.
/// </summary>
internal sealed class GetTickets : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("support/tickets", async (
            string? status,
            string? category,
            Guid? assignedAgentId,
            bool? unassigned,
            DateTime? from,
            DateTime? to,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyCollection<TicketSummaryResponse>> result = await sender.Send(
                new GetTicketsQuery(
                    status,
                    category,
                    assignedAgentId,
                    unassigned ?? false,
                    from,
                    to,
                    page ?? 1,
                    pageSize ?? 20),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetTickets)
        .WithTags(Tags.Tickets);
    }
}
