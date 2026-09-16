using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using global::Stripe;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;

/// <summary>
/// The one implementation of <see cref="IPaymentWebhookParser"/> — Feature 3.8 Milestone E, §7.2.
/// Everything Stripe-shaped about an inbound webhook stops here: the signing secret, the SDK's
/// verification, and the event object's shape.
/// <para>
/// <b><c>using global::Stripe;</c> again.</b> This file's namespace ends in <c>.Stripe</c>, so a
/// plain <c>using Stripe;</c> binds to the enclosing namespace and every SDK type stops resolving —
/// the same trap <c>StripePaymentGateway</c> documents, and it catches every new file in this folder.
/// </para>
/// </summary>
internal sealed class StripeWebhookParser(
    IOptions<StripeOptions> options,
    ILogger<StripeWebhookParser> logger) : IPaymentWebhookParser
{
    private readonly StripeOptions _options = options.Value;

    public Result<PaymentWebhookEvent> Parse(string payload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            // Not a signature failure — a deployment failure, and worth distinguishing because the
            // two look identical from the outside. Outside Development the host refuses to start
            // without this key (AddRequiredConfiguration); in Development it is the missing user
            // secret, which is exactly when somebody is staring at a wall of rejected webhooks.
            logger.LogError(
                "Stripe:WebhookSecret is not configured, so no webhook can be verified. In local " +
                "development it comes from user secrets and is printed by `stripe listen`.");

            return Result.Failure<PaymentWebhookEvent>(StripeEventLogErrors.SignatureInvalid);
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            // The SDK's verification takes a non-nullable header and an absent one is not a
            // signature that failed to verify — it is a caller that did not present one at all. The
            // validator already refuses it at the boundary; this is the seam refusing to be the
            // place a null becomes an exception.
            return Result.Failure<PaymentWebhookEvent>(StripeEventLogErrors.SignatureInvalid);
        }

        Event stripeEvent;

        try
        {
            // throwOnApiVersionMismatch: false. The account's API version is set in the Stripe
            // dashboard and the SDK's is set by the package reference; they drift independently, and
            // a mismatch is a reason to read the payload carefully, not to reject a legitimately
            // signed event. The fields this parser reads have been stable across versions for years.
            stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                _options.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException exception)
        {
            // Debug, not warning: the caller logs the rejection once at warning, and this line is
            // the detail somebody turns on when a *genuine* delivery is being refused. Logged at all
            // because the alternative — an exception message swallowed at the seam — is what makes a
            // wrong secret take an afternoon to find.
            logger.LogDebug(exception, "A Stripe webhook signature did not verify");

            return Result.Failure<PaymentWebhookEvent>(StripeEventLogErrors.SignatureInvalid);
        }

        return Project(stripeEvent);
    }

    /// <summary>
    /// Reduces the SDK's event to the neutral facts (§7.5): the identifiers, and the status
    /// <em>on this payload</em> — never anything derived from when the delivery arrived.
    /// </summary>
    private static Result<PaymentWebhookEvent> Project(Event stripeEvent)
    {
        (string? objectId, string? status, string? customer, string? paymentMethod, string? order, string? failure) =
            stripeEvent.Data?.Object switch
            {
                SetupIntent setupIntent => (
                    setupIntent.Id,
                    setupIntent.Status,
                    setupIntent.CustomerId,
                    setupIntent.PaymentMethodId,
                    (string?)null,
                    (string?)null),

                // Milestone F. Two things here do not come off a SetupIntent and are the reason the
                // arms below it work at all: the order id this service wrote into the intent's
                // metadata when it created it, and the failure mapped onto the bounded reason set
                // (§8.4) — mapped HERE, in the seam, because translating the provider's vocabulary
                // is the seam's job and nothing above it may see a StripeError.
                PaymentIntent paymentIntent => (
                    paymentIntent.Id,
                    paymentIntent.Status,
                    paymentIntent.CustomerId,
                    paymentIntent.PaymentMethodId,
                    OrderReference(paymentIntent),
                    FailureReason(paymentIntent)),

                // §10 adds Charge/Refund. An object type this seam does not model is still a
                // recorded event — the identifiers are simply unknown, and the dispatch table has no
                // arm for its type anyway.
                _ => (null, null, null, null, null, null)
            };

        return new PaymentWebhookEvent(
            stripeEvent.Id,
            stripeEvent.Type,
            objectId,
            status,
            customer,
            paymentMethod,
            order,
            failure);
    }

    /// <summary>
    /// This platform's own order id, as echoed back on the intent's metadata. Absent for an intent
    /// something else created, which is a fact to carry rather than a parse failure — the arms fall
    /// back to a local lookup by <c>pi_…</c>.
    /// </summary>
    private static string? OrderReference(PaymentIntent paymentIntent) =>
        paymentIntent.Metadata?.GetValueOrDefault("order_id");

    /// <summary>
    /// The bounded reason, and only when the payload actually carries a refusal. The mapping itself
    /// answers for a null error — a failure event with no detail is still a decline — but that
    /// answer must not be written onto an event that <em>succeeded</em>: an authorization carrying
    /// "card_declined" in a column is a row an operator will misread at three in the morning.
    /// </summary>
    private static string? FailureReason(PaymentIntent paymentIntent) =>
        paymentIntent.LastPaymentError is null
            ? null
            : StripeErrorMapping.FailureReason(paymentIntent.LastPaymentError);
}
