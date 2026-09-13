using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Payments.Application;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Payments.Application.PaymentMethods.AttachPaymentMethod;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FoodDeliveryService.Modules.Payments.Presentation.PaymentMethods;

/// <summary>
/// <b>Development only</b>, and temporary — §6.4.
/// <para>
/// Saving a card properly means confirming a SetupIntent with Stripe.js in a browser, and there is
/// no browser yet (§0.2). Without this endpoint nothing in a running system can produce a saved
/// card, so the authorize-on-placement path in §8 would be undemonstrable outside the test suite.
/// It attaches one of Stripe's server-side <b>test tokens</b> (<c>pm_card_visa</c> and its
/// siblings) — a token, never a card number, so it does not widen the PCI scope §0.5 fixes.
/// </para>
/// <para>
/// <b>Delete it when the Angular card flow lands.</b> The gate below is the environment, not a
/// permission: a permission can be granted, and this is not an endpoint anybody should be able to
/// grant themselves in production.
/// </para>
/// </summary>
internal sealed class AttachTestPaymentMethod : IEndpoint
{
    /// <summary>Stripe's Visa test token — the card <c>4242 4242 4242 4242</c>, which always succeeds.</summary>
    private const string DefaultTestPaymentMethod = "pm_card_visa";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Read from the container rather than injected into the handler: this decides whether the
        // route exists at all, so it has to be answered while the route table is being built.
        var environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>();

        if (!environment.IsDevelopment())
        {
            return;
        }

        app.MapPost("payments/payment-methods/test-cards", async (
            Request? request,
            IPaymentsContext paymentsContext,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            // The customer is the authenticated caller, exactly as on every other endpoint here.
            // Being a development affordance is not a reason to let a body name somebody else.
            Result<Guid> result = await sender.Send(
                new AttachPaymentMethodCommand(
                    paymentsContext.UserId,
                    request?.StripePaymentMethodId ?? DefaultTestPaymentMethod),
                cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.ManagePaymentMethods)
        .WithTags(Tags.PaymentMethods)
        .WithSummary("Attach a Stripe test card (Development only)")
        .WithDescription(
            "Attaches one of Stripe's server-side test tokens to the authenticated customer, so the " +
            "payment flow can be demonstrated before the browser card form exists. Mapped only when " +
            "the host runs in Development, and removed once Stripe.js collects cards for real. " +
            "Defaults to pm_card_visa; pm_card_visa_chargeDeclined and pm_card_threeDSecure2Required " +
            "are the useful alternatives.")
        .Produces<Guid>();
    }

    internal sealed class Request
    {
        /// <summary>A Stripe test token (<c>pm_…</c>). Never a card number — this API takes none.</summary>
        public string StripePaymentMethodId { get; init; } = DefaultTestPaymentMethod;
    }
}
