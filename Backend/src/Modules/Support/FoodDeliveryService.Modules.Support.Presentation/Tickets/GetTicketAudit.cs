using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketAudit;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Tickets;

/// <summary>
/// Everything that was done to one ticket, newest first.
/// <para>
/// Gated on <c>support-tickets:manage</c> rather than on <c>support-tickets:read</c>, which is the
/// difference between this and every other ticket read: the entries carry the internal reasons
/// agents wrote for each other, so this is staff-only and there is no ownership-scoped variant of it
/// a customer could reach.
/// </para>
/// </summary>
internal sealed class GetTicketAudit : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("support/tickets/{id:guid}/audit", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyCollection<SupportAuditEntryResponse>> result = await sender.Send(
                new GetTicketAuditQuery(id),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageTickets)
        .WithTags(Tags.Tickets);
    }
}
