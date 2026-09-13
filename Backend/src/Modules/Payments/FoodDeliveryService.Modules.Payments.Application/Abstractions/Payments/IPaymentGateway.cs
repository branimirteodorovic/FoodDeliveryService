using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Domain;

namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// The payment provider, as the rest of this module is allowed to see it — §5.2. One implementation
/// talks to Stripe; the integration tests substitute a fake, which is what makes every milestone
/// after this one testable without a network.
/// <para>
/// <b>Every method takes an explicit <c>idempotencyKey</c>, and that is the design.</b> Computing
/// one inside the implementation would be less typing and would make the omission possible again
/// somewhere else; as a required parameter, forgetting it is a compile error rather than a
/// double charge discovered in a bank statement. Build them with
/// <see cref="PaymentIdempotencyKeys"/> — never inline, and never from anything that changes
/// between attempts.
/// </para>
/// <para>
/// <b>Failure is split in two on purpose.</b> A returned <c>Result.Failure</c> is a business
/// outcome that retrying cannot improve — a decline, a rejected request — and the caller decides
/// what it means. A thrown <c>Common.Application.Exceptions.ApplicationException</c> is a transient
/// provider fault; see the note in <c>StripePaymentGateway</c> about what the outbox and inbox jobs
/// actually do with it, which is not what §5.3 assumed.
/// </para>
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates the Stripe customer a card is later attached to. Called for every registered user
    /// (§6.2) — a customer object with no payment method costs nothing and removes a lazy-creation
    /// race from the SetupIntent path.
    /// </summary>
    Task<Result<GatewayCustomer>> CreateCustomerAsync(
        Guid userId,
        string email,
        string? name,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts card collection. The returned client secret goes to the browser; the resulting
    /// attachment arrives as a webhook, not as the answer to this call (§7).
    /// </summary>
    Task<Result<GatewaySetupIntent>> CreateSetupIntentAsync(
        string stripeCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an already-collected card against the customer at the provider, and reads back the
    /// display fields. Milestone D, §6.3.
    /// <para>
    /// In production the browser has already confirmed a SetupIntent, so this is the reconciling
    /// call the webhook makes rather than the moment of collection; the Development-only endpoint in
    /// §6.4 uses it directly with a Stripe test token, because nothing can drive Stripe.js yet.
    /// </para>
    /// </summary>
    Task<Result<GatewayPaymentMethod>> AttachPaymentMethodAsync(
        string stripeCustomerId,
        string paymentMethodId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a saved card at the provider. Called before the local row is cleared: the other
    /// order leaves this platform claiming a card the provider has already let go.
    /// </summary>
    Task<Result> DetachPaymentMethodAsync(
        string paymentMethodId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places a hold: a manual-capture PaymentIntent, confirmed off-session against the saved card.
    /// Success means the funds are reserved and <b>not</b> taken —
    /// <see cref="GatewayPaymentIntentStatus.RequiresCapture"/>.
    /// </summary>
    Task<Result<GatewayPaymentIntent>> AuthorizeAsync(
        GatewayAuthorization authorization,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Takes the held funds, in full. Partial captures are out of scope (§11.2).</summary>
    Task<Result<GatewayPaymentIntent>> CaptureAsync(
        string paymentIntentId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a hold that will never be captured — the restaurant rejected the order, or the
    /// customer cancelled it. Nothing was ever charged, so this is a cancellation at Stripe rather
    /// than a refund.
    /// </summary>
    Task<Result<GatewayPaymentIntent>> ReleaseAsync(
        string paymentIntentId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns money that was actually captured (§10). The amount is checked twice before it gets
    /// here — Support caps the request at the replicated order subtotal, and §10.1 caps it again at
    /// what was captured minus what has already been refunded.
    /// </summary>
    Task<Result<GatewayRefund>> RefundAsync(
        string paymentIntentId,
        Money amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
