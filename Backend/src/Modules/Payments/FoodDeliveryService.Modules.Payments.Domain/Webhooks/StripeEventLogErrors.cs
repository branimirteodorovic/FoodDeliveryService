using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Webhooks;

public static class StripeEventLogErrors
{
    /// <summary>
    /// The signature did not verify, or the body was not a provider event at all.
    /// <para>
    /// <see cref="ErrorType.Problem"/>, so it surfaces as a <c>400</c> and Stripe redelivers. A
    /// forged request never verifies and will keep failing, which is correct — a genuine delivery
    /// always carries a valid signature, so the only requests this refuses are the ones that should
    /// be refused. The description names no detail of the failure: a caller probing for the
    /// difference between "wrong secret" and "wrong digest" is a caller trying to forge one.
    /// </para>
    /// </summary>
    public static readonly Error SignatureInvalid = Error.Problem(
        "Payments.WebhookSignatureInvalid",
        "The webhook signature could not be verified");

    public static readonly Error ProviderEventIdRequired = Error.Problem(
        "Payments.WebhookEventIdRequired",
        "A provider event identifier is required");

    public static readonly Error EventTypeRequired = Error.Problem(
        "Payments.WebhookEventTypeRequired",
        "A provider event type is required");

    /// <summary>
    /// The outbox went looking for a row the endpoint wrote and did not find it. Only reachable if
    /// the row was deleted between the insert and the dispatch, which nothing does.
    /// </summary>
    public static Error NotFound(Guid eventLogId) => Error.NotFound(
        "Payments.WebhookEventNotFound",
        $"No recorded webhook event with the identifier {eventLogId} was found");

    /// <summary>
    /// A <c>setup_intent.succeeded</c> that names a customer this service has no profile for. Either
    /// the <c>UserRegistered</c> event has not been consumed yet, or the card was saved against a
    /// Stripe customer some other system created.
    /// </summary>
    public static Error CustomerProfileNotFound(string customerReference) => Error.NotFound(
        "Payments.WebhookCustomerProfileNotFound",
        $"No payment profile is linked to the provider customer {customerReference}");

    /// <summary>
    /// The event arrived without the field the work needs — a <c>setup_intent.succeeded</c> whose
    /// payload names no payment method, say. Recorded rather than thrown: the row is written either
    /// way, and this is what the operator reads off it.
    /// </summary>
    public static Error EventIncomplete(string eventType, string field) => Error.Problem(
        "Payments.WebhookEventIncomplete",
        $"The {eventType} event carried no {field}");
}
