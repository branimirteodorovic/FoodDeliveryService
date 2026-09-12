namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// The provider's view of a payment, mapped off Stripe's <c>PaymentIntent.status</c> string.
/// <para>
/// Modelled as an enum rather than passed through as the raw string for the same reason
/// <see cref="Domain.Payments.PaymentFailureReason"/> is bounded: these values reach metric tags and
/// log properties, and an unrecognised status must be a value the code can name rather than a new
/// time series. The American spelling of <see cref="Canceled"/> is Stripe's, kept deliberately —
/// this enum mirrors a provider vocabulary, and translating it to the platform's own
/// <c>Cancelled</c> would suggest the two are the same thing.
/// </para>
/// </summary>
public enum GatewayPaymentIntentStatus
{
    /// <summary>Stripe returned a status this seam does not model. Never treated as success.</summary>
    Unknown = 0,
    RequiresPaymentMethod = 1,
    RequiresConfirmation = 2,

    /// <summary>3-D Secure wants the cardholder. Off-session, this is a failure (§8.5).</summary>
    RequiresAction = 3,
    Processing = 4,

    /// <summary>The authorization is held and awaiting capture — the happy state after §8.</summary>
    RequiresCapture = 5,
    Canceled = 6,

    /// <summary>Captured: the money has actually moved.</summary>
    Succeeded = 7
}
