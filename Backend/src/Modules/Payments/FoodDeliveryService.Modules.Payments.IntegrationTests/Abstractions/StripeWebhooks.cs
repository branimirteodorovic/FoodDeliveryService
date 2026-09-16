using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;

/// <summary>
/// Builds and signs webhook deliveries the way Stripe does — Feature 3.8 Milestones E and F.
/// <para>
/// <b><c>IPaymentWebhookParser</c> is the one piece of the Stripe seam this suite does not
/// substitute.</b> It is the only authentication the webhook endpoint has, and a faked verification
/// would assert nothing about the thing being verified. So the tests sign their own payloads under
/// the fixture secret and the real parser checks them.
/// </para>
/// <para>
/// It lives here, shared, because two test classes now deliver webhooks and a second copy of a
/// signing routine is a copy that drifts — at which point one of them starts failing for a reason
/// that has nothing to do with what it was testing.
/// </para>
/// </summary>
internal static class StripeWebhooks
{
    public const string Path = "payments/webhooks/stripe";

    public const string SignatureHeader = "Stripe-Signature";

    /// <summary>
    /// Stripe's signing scheme: <c>t={unix},v1={hex HMAC-SHA256 of "{t}.{payload}"}</c>. Written out
    /// rather than mocked, for the reason in the type remarks.
    /// </summary>
    public static string Sign(
        string payload,
        string secret = IntegrationTestWebAppFactory.WebhookSigningSecret)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        byte[] digest = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{payload}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"t={timestamp},v1={Convert.ToHexStringLower(digest)}");
    }

    /// <summary>
    /// Posts a delivery with no bearer token anywhere: the endpoint is anonymous, and the signature
    /// is the credential. Pass <see cref="string.Empty"/> to send no signature header at all.
    /// </summary>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string payload,
        string? signature,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Path, UriKind.Relative))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        signature ??= Sign(payload);

        if (signature.Length > 0)
        {
            request.Headers.Add(SignatureHeader, signature);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>A fresh event id per delivery, so the dedupe index is never the reason a test fails.</summary>
    public static string NewEventId() => $"evt_{Guid.NewGuid():N}";

    /// <summary>
    /// A <c>payment_intent.amount_capturable_updated</c> — the provider's own account of a hold,
    /// carrying the <c>order_id</c> metadata this service wrote when it created the intent.
    /// </summary>
    public static string AmountCapturableUpdated(string eventId, string paymentIntentId, Guid orderId) =>
        $$"""
          {
            "id": "{{eventId}}",
            "object": "event",
            "type": "payment_intent.amount_capturable_updated",
            "data": {
              "object": {
                "id": "{{paymentIntentId}}",
                "object": "payment_intent",
                "status": "requires_capture",
                "amount": 2500,
                "currency": "eur",
                "metadata": { "order_id": "{{orderId}}" }
              }
            }
          }
          """;

    /// <summary>
    /// A <c>payment_intent.payment_failed</c>, carrying the <c>last_payment_error</c> the parser maps
    /// onto the bounded reason set.
    /// </summary>
    public static string PaymentFailed(
        string eventId,
        string paymentIntentId,
        Guid orderId,
        string declineCode) =>
        $$"""
          {
            "id": "{{eventId}}",
            "object": "event",
            "type": "payment_intent.payment_failed",
            "data": {
              "object": {
                "id": "{{paymentIntentId}}",
                "object": "payment_intent",
                "status": "requires_payment_method",
                "amount": 2500,
                "currency": "eur",
                "metadata": { "order_id": "{{orderId}}" },
                "last_payment_error": {
                  "type": "card_error",
                  "code": "card_declined",
                  "decline_code": "{{declineCode}}"
                }
              }
            }
          }
          """;
}
