using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Refunds.GetRefundRequests;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Refunds;

/// <summary>
/// The approval queue (<c>?status=Requested</c>) and the refund history behind it.
/// <para>
/// Gated on <c>refunds:request</c> rather than <c>refunds:approve</c>: an agent needs to see that
/// what they asked for was decided, and reading the queue is not deciding on it. No customer holds
/// either code, so this list never reaches one.
/// </para>
/// </summary>
internal sealed class GetRefundRequests : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("support/refund-requests", async (
            string? status,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyCollection<RefundRequestResponse>> result = await sender.Send(
                new GetRefundRequestsQuery(status, page ?? 1, pageSize ?? 20),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.RequestRefund)
        .WithTags(Tags.Refunds);
    }
}
