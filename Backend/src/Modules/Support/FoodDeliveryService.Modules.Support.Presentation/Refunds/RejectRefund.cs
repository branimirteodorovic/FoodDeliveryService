using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Refunds.RejectRefund;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Refunds;

/// <summary>
/// An administrator declines a refund. Gated on the same <c>refunds:approve</c> code as the
/// approval: deciding is one authority, and an account that could decline but not approve would be
/// an odd thing to grant. The customer is emailed either way — a request that is refused in silence
/// is indistinguishable, from the customer's side, from one nobody read.
/// </summary>
internal sealed class RejectRefund : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("support/refund-requests/{id:guid}/reject", async (
            Guid id,
            Request request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new RejectRefundCommand(id, request.Note), cancellationToken);

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
