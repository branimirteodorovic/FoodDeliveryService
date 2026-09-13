using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Payments.Application;
using FoodDeliveryService.Modules.Payments.Application.PaymentMethods.GetPaymentMethods;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Payments.Presentation.PaymentMethods;

/// <summary>
/// The caller's own saved cards. There is no route or query parameter naming a customer, so there is
/// no shape in which one customer can ask for another's.
/// </summary>
internal sealed class GetPaymentMethods : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("payments/payment-methods", async (
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyCollection<PaymentMethodResponse>> result = await sender.Send(
                new GetPaymentMethodsQuery(),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManagePaymentMethods)
        .WithTags(Tags.PaymentMethods)
        .WithSummary("List your saved cards")
        .WithDescription(
            "The authenticated customer's saved cards - brand, last four digits and expiry, which " +
            "is everything this platform stores about a card. At most one is saved at a time: " +
            "saving another replaces it. Empty until Stripe confirms the card entered against a " +
            "SetupIntent.")
        .Produces<IReadOnlyCollection<PaymentMethodResponse>>();
    }
}
