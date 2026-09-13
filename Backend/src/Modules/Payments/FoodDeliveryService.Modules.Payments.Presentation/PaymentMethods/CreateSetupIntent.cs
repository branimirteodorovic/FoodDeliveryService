using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Payments.Application;
using FoodDeliveryService.Modules.Payments.Application.PaymentMethods.CreateSetupIntent;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Payments.Presentation.PaymentMethods;

/// <summary>
/// Starts card collection. The browser takes the returned client secret to Stripe.js, which collects
/// the card and confirms it directly against Stripe — this backend never sees a card number, which
/// is what keeps it at PCI SAQ-A.
/// </summary>
internal sealed class CreateSetupIntent : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("payments/payment-methods/setup-intents", async (
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result<SetupIntentResponse> result = await sender.Send(
                new CreateSetupIntentCommand(),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManagePaymentMethods)
        .WithTags(Tags.PaymentMethods)
        .WithSummary("Start saving a card")
        .WithDescription(
            "Creates a Stripe SetupIntent for the authenticated customer and returns its client " +
            "secret for Stripe.js. The card is collected and confirmed in the browser, against " +
            "Stripe directly - no card number ever reaches this API. **This call is not the " +
            "attachment**: the card is saved when Stripe's setup_intent.succeeded webhook arrives, " +
            "so a client polls GET payments/payment-methods rather than assuming success here.")
        .Produces<SetupIntentResponse>();
    }
}
