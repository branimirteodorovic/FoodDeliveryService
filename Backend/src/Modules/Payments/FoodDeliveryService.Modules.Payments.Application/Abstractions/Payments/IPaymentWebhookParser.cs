using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// Verifies a provider webhook's signature and projects its payload onto
/// <see cref="PaymentWebhookEvent"/> — Feature 3.8 Milestone E, §7.2.
/// <para>
/// <b>This abstraction exists because §7.2's sketch does not compile in this solution.</b> That
/// sketch calls <c>EventUtility.ConstructEvent</c> inside the endpoint; the endpoint lives in
/// <c>Payments.Presentation</c>, which <c>Payments.Infrastructure</c> <em>references</em>, so
/// Presentation can see neither the Stripe SDK nor <c>StripeOptions</c>. Inverting the reference
/// would put the SDK in the assembly every other module's endpoints are modelled on. So the
/// verification sits here, behind bytes and a header, and is implemented next to the API key.
/// </para>
/// <para>
/// <b>The endpoint still reads the raw body itself, and that part is not negotiable.</b> The
/// signature covers the exact bytes Stripe sent: bind a model and the stream is consumed, and
/// re-serializing the bound object changes the bytes — so the digest will not match and every
/// webhook is rejected, which looks exactly like a wrong secret.
/// </para>
/// </summary>
public interface IPaymentWebhookParser
{
    /// <summary>
    /// Returns the event, or a failure if the signature does not verify.
    /// </summary>
    /// <param name="payload">The request body, byte for byte as it arrived.</param>
    /// <param name="signature">The provider's signature header, verbatim.</param>
    Result<PaymentWebhookEvent> Parse(string payload, string? signature);
}
