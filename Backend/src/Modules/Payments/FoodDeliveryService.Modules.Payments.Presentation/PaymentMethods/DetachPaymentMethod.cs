using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Payments.Application;
using FoodDeliveryService.Modules.Payments.Application.PaymentMethods.DetachPaymentMethod;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Payments.Presentation.PaymentMethods;

/// <summary>
/// Removes the caller's saved card, at Stripe and then here. The id in the route is this platform's
/// identifier for the card, not Stripe's — a customer's own card id is not a provider credential and
/// does not become one by being in a URL.
/// </summary>
internal sealed class DetachPaymentMethod : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("payments/payment-methods/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            Result result = await sender.Send(new DetachPaymentMethodCommand(id), cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManagePaymentMethods)
        .WithTags(Tags.PaymentMethods)
        .WithSummary("Remove a saved card")
        .WithDescription(
            "Detaches the card at Stripe and then forgets it here, in that order - the reverse " +
            "would leave this platform able to charge a card the customer believes is gone. A card " +
            "id that is not the caller's own is a 404. Orders stops offering card payment to this " +
            "customer once the resulting event is projected.")
        .Produces(StatusCodes.Status204NoContent);
    }
}
