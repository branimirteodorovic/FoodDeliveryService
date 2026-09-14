using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;
using FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// Feature 3.8 Milestone E, §7.2 — the inbound half of the Stripe seam.
/// <para>
/// The signature is the <b>only</b> authentication this platform's webhook endpoint has, so these
/// tests are the ones that say the endpoint is not simply open. They sign their own payloads exactly
/// as Stripe does — <c>HMAC-SHA256</c> over <c>{timestamp}.{payload}</c> — rather than mocking the
/// verification, because a mocked verification proves nothing about the thing being verified.
/// </para>
/// </summary>
public class StripeWebhookParserTests
{
    // Short segments on purpose: SecretHygieneTests fails the build on `whsec_` followed by 16 or
    // more unbroken alphanumerics, so a realistic-looking fixture secret is not available here
    // (Milestone C, §5.6).
    private const string WebhookSecret = "whsec_not_a_real_secret";

    private const string SetupIntentSucceeded =
        """
        {
          "id": "evt_parser_tests",
          "object": "event",
          "type": "setup_intent.succeeded",
          "data": {
            "object": {
              "id": "seti_parser_tests",
              "object": "setup_intent",
              "status": "succeeded",
              "customer": "cus_parser_tests",
              "payment_method": "pm_parser_tests"
            }
          }
        }
        """;

    private static StripeWebhookParser Parser(string secret = WebhookSecret) =>
        new(
            Options.Create(new StripeOptions { SecretKey = "sk_test_not_a_real_key", WebhookSecret = secret }),
            NullLogger<StripeWebhookParser>.Instance);

    /// <summary>
    /// Stripe's scheme, reproduced: <c>t={unix},v1={hex HMAC-SHA256 of "t.payload" under the signing
    /// secret}</c>. Writing it out here is what makes "the raw bytes matter" testable — re-serialize
    /// the payload between here and the parser and the digest stops matching.
    /// </summary>
    private static string Sign(string payload, string secret = WebhookSecret, DateTime? at = null)
    {
        long timestamp = new DateTimeOffset(at ?? DateTime.UtcNow).ToUnixTimeSeconds();

        byte[] digest = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(
                string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{payload}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"t={timestamp},v1={Convert.ToHexStringLower(digest)}");
    }

    [Fact]
    public void AVerifiedEvent_Should_ProjectOntoTheNeutralFacts()
    {
        Result<PaymentWebhookEvent> result = Parser().Parse(
            SetupIntentSucceeded,
            Sign(SetupIntentSucceeded));

        result.IsSuccess.Should().BeTrue();

        PaymentWebhookEvent webhookEvent = result.Value;

        webhookEvent.EventId.Should().Be("evt_parser_tests");
        webhookEvent.EventType.Should().Be(PaymentWebhookEventTypes.SetupIntentSucceeded);
        webhookEvent.ObjectId.Should().Be("seti_parser_tests");

        // §7.5: the status comes off the payload, and it is the only thing a handler may branch on.
        webhookEvent.ObjectStatus.Should().Be("succeeded");

        webhookEvent.CustomerReference.Should().Be("cus_parser_tests");
        webhookEvent.PaymentMethodReference.Should().Be("pm_parser_tests");
    }

    [Fact]
    public void APayloadSignedWithAnotherSecret_Should_BeRefused()
    {
        // The production version of this is a webhook secret from the wrong environment: a signature
        // Stripe generated, over a payload Stripe sent, that this host must still refuse.
        Result<PaymentWebhookEvent> result = Parser().Parse(
            SetupIntentSucceeded,
            Sign(SetupIntentSucceeded, secret: "whsec_a_different_secret"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StripeEventLogErrors.SignatureInvalid);
    }

    [Fact]
    public void APayloadChangedAfterSigning_Should_BeRefused()
    {
        // The tamper case, and the reason the endpoint reads the raw stream instead of binding a
        // model: the digest covers the exact bytes, so a single character makes it fail.
        string signature = Sign(SetupIntentSucceeded);

        Result<PaymentWebhookEvent> result = Parser().Parse(
            SetupIntentSucceeded.Replace("cus_parser_tests", "cus_somebody_else", StringComparison.Ordinal),
            signature);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StripeEventLogErrors.SignatureInvalid);
    }

    [Fact]
    public void AStaleSignature_Should_BeRefused()
    {
        // Stripe's timestamp tolerance, which is what stops a captured delivery being replayed days
        // later. It is the SDK's default rather than ours, and this pins that we did not turn it off.
        Result<PaymentWebhookEvent> result = Parser().Parse(
            SetupIntentSucceeded,
            Sign(SetupIntentSucceeded, at: DateTime.UtcNow.AddHours(-1)));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StripeEventLogErrors.SignatureInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("t=1,v1=notahexdigest")]
    public void AMissingOrMalformedSignature_Should_BeRefused(string? signature)
    {
        Result<PaymentWebhookEvent> result = Parser().Parse(SetupIntentSucceeded, signature);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StripeEventLogErrors.SignatureInvalid);
    }

    [Fact]
    public void AnUnconfiguredWebhookSecret_Should_RefuseEverything()
    {
        // A deployment failure rather than a signature failure, and the one that looks identical
        // from outside. Verifying against an empty secret would accept anything that could compute
        // an HMAC under "" — which is everyone.
        Result<PaymentWebhookEvent> result = Parser(secret: string.Empty).Parse(
            SetupIntentSucceeded,
            Sign(SetupIntentSucceeded));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StripeEventLogErrors.SignatureInvalid);
    }

    [Fact]
    public void AnEventThisSeamDoesNotModel_Should_StillBeRecorded()
    {
        // The log is a record of what the provider said, not of what this module chose to act on.
        // Turning an unknown object type into a failure would mean every event type somebody enables
        // in the Stripe dashboard takes this endpoint to a 400 and Stripe into backoff.
        const string payload =
            """
            {
              "id": "evt_unmodelled",
              "object": "event",
              "type": "customer.updated",
              "data": { "object": { "id": "cus_parser_tests", "object": "customer" } }
            }
            """;

        Result<PaymentWebhookEvent> result = Parser().Parse(payload, Sign(payload));

        result.IsSuccess.Should().BeTrue();
        result.Value.EventId.Should().Be("evt_unmodelled");
        result.Value.EventType.Should().Be("customer.updated");
        result.Value.ObjectId.Should().BeNull();
    }
}
