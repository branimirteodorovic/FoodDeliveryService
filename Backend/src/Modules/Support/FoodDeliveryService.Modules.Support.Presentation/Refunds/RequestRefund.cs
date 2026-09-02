using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Refunds.RequestRefund;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Refunds;

/// <summary>
/// An agent asks for the customer on this ticket to be refunded. Nested under the ticket because a
/// refund only ever exists as the outcome of a case: the order and the customer are read from the
/// ticket, so there is no route or body field on which an agent could name a different one.
/// </summary>
internal sealed class RequestRefund : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("support/tickets/{id:guid}/refund-requests", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<Guid> result = await sender.Send(
                new RequestRefundCommand(id, request.Amount, request.Reason),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.RequestRefund)
        .WithTags(Tags.Refunds);
    }

    internal sealed class Request
    {
        /// <summary>Capped at the replicated order subtotal by the aggregate, not by this type.</summary>
        public decimal Amount { get; init; }

        /// <summary>
        /// Required. The reason is what the approving administrator decides on, and it is the half
        /// of the audit entry that the amount cannot supply.
        /// </summary>
        public string Reason { get; init; } = string.Empty;
    }
}
