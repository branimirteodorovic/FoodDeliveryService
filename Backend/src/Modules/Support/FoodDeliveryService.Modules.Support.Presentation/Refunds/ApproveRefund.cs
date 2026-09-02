using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Refunds.ApproveRefund;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Refunds;

/// <summary>
/// An administrator agrees to a refund. <c>refunds:approve</c> is admin-only, which keeps agents off
/// this route entirely — and the aggregate refuses the requester as well, which is what covers the
/// case the permission cannot see: an administrator deciding on a request they raised themselves.
/// <para>
/// No route on this service pays anybody. Approval records a decision; the platform has no payment
/// processing, and nothing consumes the event but the customer's email.
/// </para>
/// </summary>
internal sealed class ApproveRefund : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("support/refund-requests/{id:guid}/approve", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new ApproveRefundCommand(id, request.Note), cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ApproveRefund)
        .WithTags(Tags.Refunds);
    }

    internal sealed class Request
    {
        /// <summary>Optional; recorded on the refund request and on the audit entry when supplied.</summary>
        public string? Note { get; init; }
    }
}
