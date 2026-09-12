using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using global::Stripe;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;

/// <summary>
/// Turns a <see cref="StripeException"/> into either an <see cref="Error"/> the caller decides about
/// or a signal that the call should be treated as a fault — §5.3. Which of the two it is decides
/// whether the platform ever tries again, so it is worth reading the table twice.
/// </summary>
internal static class StripeErrorMapping
{
    /// <summary>
    /// True when the failure is transient — a network blip, a Stripe outage, a rate limit, any 5xx.
    /// The caller throws on these so the message is recorded as a fault rather than silently
    /// absorbed as a business outcome.
    /// </summary>
    public static bool IsTransient(StripeException exception)
    {
        if ((int)exception.HttpStatusCode >= 500)
        {
            return true;
        }

        return exception.StripeError?.Type switch
        {
            "api_connection_error" or "api_error" or "rate_limit_error" => true,
            _ => false
        };
    }

    /// <summary>
    /// The permanent failures. A decline carries the bounded reason; everything else is our own bug
    /// and says so, because retrying it produces the same answer and the useful response is an
    /// alert rather than a queue.
    /// </summary>
    public static Error ToError(StripeException exception)
    {
        StripeError? error = exception.StripeError;

        return error?.Type switch
        {
            "card_error" => PaymentErrors.Declined(DeclineReason(error)),

            // Wrong parameters, or — the one worth recognising — an idempotency key reused with a
            // different payload, which means two code paths built the same key for different work.
            "invalid_request_error" or "idempotency_error" =>
                PaymentErrors.GatewayRequestInvalid(error.Code ?? error.Type),

            "authentication_error" => PaymentErrors.GatewayNotAuthenticated,

            // No StripeError at all, or a type this mapping does not know. Not transient (the caller
            // asked IsTransient first), so it is a permanent failure of unknown shape.
            _ => PaymentErrors.GatewayRequestInvalid(error?.Type ?? "unknown")
        };
    }

    /// <summary>
    /// Stripe reports a decline twice: <c>code</c> is the API-level classification and
    /// <c>decline_code</c> is what the issuer said. The issuer's answer is the specific one, so it
    /// is read first and only falls back to the code — but both are mapped onto the five values in
    /// <see cref="PaymentFailureReason"/> and never forwarded raw, because Stripe publishes dozens
    /// of decline codes and every one of them would become a metric label (§8.4).
    /// </summary>
    private static string DeclineReason(StripeError error)
    {
        if (error.Code == "authentication_required")
        {
            // Off-session 3-D Secure. Reached here because AuthorizeAsync sets ErrorOnRequiresAction,
            // which turns the requires_action status into this card_error instead (§8.5).
            return PaymentFailureReason.AuthenticationRequired;
        }

        return error.DeclineCode switch
        {
            "insufficient_funds" => PaymentFailureReason.InsufficientFunds,
            "expired_card" => PaymentFailureReason.ExpiredCard,
            "authentication_required" => PaymentFailureReason.AuthenticationRequired,
            _ => error.Code switch
            {
                "expired_card" => PaymentFailureReason.ExpiredCard,
                // Everything else the issuer refused — do_not_honor, fraudulent, lost_card,
                // pickup_card and the rest. Telling a customer which of those it was is a fraud
                // signal, not a service.
                _ => PaymentFailureReason.CardDeclined
            }
        };
    }
}
