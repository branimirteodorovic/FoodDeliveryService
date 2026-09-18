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
/// No route on this service moves money itself, and approving is still recording a decision rather
/// than performing a transfer. What changed with Feature 3.8 is what happens next: the approval is
/// published, the Payments service refunds the captured card payment against it, and the request
/// comes back Settled or Failed. The authority to do any of that is this endpoint and the aggregate
/// behind it — Payments performs no check of its own on who agreed.
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
        .WithTags(Tags.Refunds)
        .WithSummary("Approve a refund request")
        .WithDescription(
            "An administrator approves - and it must be a **different** person than the agent who " +
            "requested it. That segregation of duties is enforced in the aggregate, not by the " +
            "permission, so it cannot be granted around.")
        .Produces(StatusCodes.Status204NoContent);
    }

    internal sealed class Request
    {
        /// <summary>Optional; recorded on the refund request and on the audit entry when supplied.</summary>
        public string? Note { get; init; }
    }
}
