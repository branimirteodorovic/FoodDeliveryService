using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Payments.Application.Webhooks.RecordWebhookEvent;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Payments.Presentation.Webhooks;

/// <summary>
/// Stripe's ingress — Feature 3.8 Milestone E, §7.
/// <para>
/// <b>Anonymous, because the signature is the credential.</b> Stripe holds no token for this
/// platform and cannot be given one; what it holds is a shared signing secret, and a request that
/// verifies against it is the only kind this endpoint acts on. The exemption is recorded in
/// <c>EndpointAuthorizationTests.AnonymousRoutes</c> and in <c>OpenApiDocumentTests</c>, as the
/// platform's other two anonymous routes are.
/// </para>
/// <para>
/// <b>It is also exempt from the edge rate limiter</b>, which is the non-obvious half (§7.3): the
/// limiter partitions anonymous callers by IP, every Stripe delivery arrives from a small set of
/// Stripe addresses, and they therefore share <em>one</em> bucket. A busy minute would be
/// <c>429</c>'d, Stripe would back off exponentially, and payment state would silently lag behind
/// reality. The rule lives in <c>RateLimitRoutePolicy</c>.
/// </para>
/// </summary>
internal sealed class StripeWebhook : IEndpoint
{
    /// <summary>Stripe's signature header, spelled as Stripe spells it.</summary>
    private const string SignatureHeader = "Stripe-Signature";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("payments/webhooks/stripe", async (
            HttpRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            // The stream is read here, by hand, and the body is NEVER bound to a model. The
            // signature covers the exact bytes Stripe sent: binding consumes the stream, and
            // re-serializing the bound object changes those bytes, so the digest stops matching and
            // every webhook is rejected. The failure mode is a 100% rejection rate that looks
            // exactly like a wrong signing secret (§7.2).
            using var reader = new StreamReader(request.Body);

            string payload = await reader.ReadToEndAsync(cancellationToken);

            Result result = await sender.Send(
                new RecordWebhookEventCommand(payload, request.Headers[SignatureHeader]),
                cancellationToken);

            return result.Match(Results.NoContent, ApiResults.Problem);
        })
        .AllowAnonymous()
        .WithTags(Tags.Webhooks)
        .WithSummary("Receive a Stripe webhook")
        .WithDescription(
            "Stripe's callback for payment and card events. Authenticated by the Stripe-Signature " +
            "header rather than by a bearer token, and verified against this environment's signing " +
            "secret - a request that does not verify is rejected with a 400 and nothing is " +
            "recorded. A verified event is written to the event log and acted on asynchronously, so " +
            "this returns immediately: Stripe times out at 20 seconds and treats a slow endpoint as " +
            "a failed one. Redeliveries are acknowledged without repeating the work, and a request " +
            "that lands here from anywhere other than Stripe simply fails to verify.")
        .Produces(StatusCodes.Status204NoContent);
    }
}
