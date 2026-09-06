using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Support.Application;
using FoodDeliveryService.Modules.Support.Application.Analytics.GetSupportSummary;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Support.Presentation.Analytics;

/// <summary>
/// The support management summary. Staff-only: <c>support-analytics:read</c> is held by agents and
/// administrators and by no customer, and unlike every other read in this module there is nothing
/// here to narrow to an owner — the numbers are platform-wide by definition.
/// <para>
/// The window defaults are applied here rather than in the handler, through
/// <see cref="GetSupportSummaryQuery.Create"/>: the query is an <c>ICachedQuery</c>, so its cache
/// key is asked for before any handler runs, and a query still holding nulls at that point would
/// key every window the same.
/// </para>
/// </summary>
internal sealed class GetSupportSummary : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("support/analytics/summary", async (
            DateTime? from,
            DateTime? to,
            ISender sender,
            IDateTimeProvider dateTimeProvider,
            CancellationToken cancellationToken) =>
        {
            Result<SupportSummaryResponse> result = await sender.Send(
                GetSupportSummaryQuery.Create(from, to, dateTimeProvider.UtcNow),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.GetAnalytics)
        .WithTags(Tags.Analytics)
        .WithSummary("Get the support summary")
        .WithDescription(
            "Ticket volumes, resolution times and refund outcomes over a window. Staff-only and " +
            "platform-wide - unlike every other read here there is no owner to narrow it to. The " +
            "window defaults are applied at the endpoint because the query is cached and its key is " +
            "computed before any handler runs.")
        .Produces<SupportSummaryResponse>();
    }
}
