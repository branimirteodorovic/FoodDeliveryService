using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

/// <summary>
/// Errors the payment gateway seam produces. The aggregate's own errors (not found, already
/// captured, refund exceeds the captured amount) join them in §8 and §10.
/// </summary>
public static class PaymentErrors
{
    /// <summary>
    /// The card was refused. A business outcome, not a fault: retrying the same call produces the
    /// same refusal, so nothing above this should treat it as transient.
    /// </summary>
    public static PaymentDeclinedError Declined(string reason) => new(
        reason,
        $"The payment was declined by the card issuer ({reason})");

    /// <summary>
    /// Stripe rejected the request itself — a parameter we sent is wrong, or an idempotency key was
    /// reused with different parameters. <see cref="ErrorType.Failure"/>, so it surfaces as a 500
    /// rather than a 400: the caller did nothing wrong and there is nothing they can change.
    /// </summary>
    public static Error GatewayRequestInvalid(string code) => Error.Failure(
        "Payments.GatewayRequestInvalid",
        $"The payment provider rejected the request ({code})");

    /// <summary>
    /// Stripe rejected the API key. Almost always a missing or stale <c>Stripe:SecretKey</c> — the
    /// symptom of forgetting the user-secret in local development, where the configuration
    /// fail-fast in <c>Program.cs</c> deliberately does not run.
    /// </summary>
    public static readonly Error GatewayNotAuthenticated = Error.Failure(
        "Payments.GatewayNotAuthenticated",
        "The payment provider rejected this service's API key");
}
