using System.Net;
using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;
using Stripe;

namespace FoodDeliveryService.Modules.Payments.UnitTests;

/// <summary>
/// Feature 3.8 Milestone C, §5.3 — the table that decides what happens to a failed payment.
/// <para>
/// It is a small piece of code with an outsized blast radius. Classify a decline as transient and
/// the platform re-presents a card the issuer has already refused; classify a Stripe outage as a
/// decline and the customer is told their card was declined when it never reached their bank, the
/// order is cancelled, and an email says so.
/// </para>
/// </summary>
public class StripeErrorMappingTests
{
    private static StripeException StripeFailure(
        string type,
        string? code = null,
        string? declineCode = null,
        HttpStatusCode statusCode = HttpStatusCode.PaymentRequired) =>
        new(
            statusCode,
            new StripeError { Type = type, Code = code, DeclineCode = declineCode },
            "stripe test error");

    [Theory]
    [InlineData("api_connection_error")]
    [InlineData("api_error")]
    [InlineData("rate_limit_error")]
    public void ProviderFaults_AreTransient(string type)
    {
        StripeErrorMapping.IsTransient(StripeFailure(type)).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void AnyFiveHundred_IsTransient(HttpStatusCode statusCode)
    {
        // Checked on the status as well as the error type, because a 5xx that arrives without a
        // parseable StripeError body has no type to classify by.
        StripeErrorMapping
            .IsTransient(StripeFailure("api_error", statusCode: statusCode))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("card_error")]
    [InlineData("invalid_request_error")]
    [InlineData("idempotency_error")]
    [InlineData("authentication_error")]
    public void PermanentFailures_AreNotTransient(string type)
    {
        StripeErrorMapping.IsTransient(StripeFailure(type)).Should().BeFalse();
    }

    [Fact]
    public void ADecline_CarriesTheReasonOnTheError()
    {
        var error = StripeErrorMapping.ToError(
            StripeFailure("card_error", code: "card_declined", declineCode: "insufficient_funds"));

        // The reason has to survive as data rather than as prose: §8.4 publishes it on an
        // integration event and §11.1 puts it on a metric tag.
        error.Should().BeOfType<PaymentDeclinedError>()
            .Which.Reason.Should().Be(PaymentFailureReason.InsufficientFunds);
    }

    [Theory]
    [InlineData("card_declined", null, PaymentFailureReason.CardDeclined)]
    [InlineData("card_declined", "insufficient_funds", PaymentFailureReason.InsufficientFunds)]
    [InlineData("expired_card", null, PaymentFailureReason.ExpiredCard)]
    [InlineData("card_declined", "expired_card", PaymentFailureReason.ExpiredCard)]
    // Off-session 3-D Secure. AuthorizeAsync sets ErrorOnRequiresAction so it arrives as a
    // card_error rather than as a requires_action status nobody can act on (§8.5).
    [InlineData("authentication_required", null, PaymentFailureReason.AuthenticationRequired)]
    // Everything else the issuer refused collapses to the generic reason. Telling a customer their
    // card was reported lost or flagged as fraudulent is a fraud signal, not a service.
    [InlineData("card_declined", "do_not_honor", PaymentFailureReason.CardDeclined)]
    [InlineData("card_declined", "lost_card", PaymentFailureReason.CardDeclined)]
    [InlineData("card_declined", "stolen_card", PaymentFailureReason.CardDeclined)]
    public void DeclineCodes_MapOntoTheBoundedSet(string? code, string? declineCode, string expected)
    {
        var error = StripeErrorMapping.ToError(StripeFailure("card_error", code, declineCode));

        error.Should().BeOfType<PaymentDeclinedError>().Which.Reason.Should().Be(expected);
        PaymentFailureReason.IsKnown(expected).Should().BeTrue();
    }

    [Theory]
    [InlineData("invalid_request_error")]
    [InlineData("idempotency_error")]
    public void OurOwnBugs_AreAFailureNotADecline(string type)
    {
        var error = StripeErrorMapping.ToError(StripeFailure(type, code: "parameter_missing"));

        error.Should().NotBeOfType<PaymentDeclinedError>();
        // ErrorType.Failure, so it surfaces as a 500 rather than a 400: the caller did nothing wrong
        // and there is nothing they can change about the request.
        error.Type.Should().Be(ErrorType.Failure);
        error.Code.Should().Be("Payments.GatewayRequestInvalid");
    }

    [Fact]
    public void ARejectedApiKey_SaysSo()
    {
        // The symptom of a missing Stripe:SecretKey user secret in local development, where the
        // configuration fail-fast in Program.cs deliberately does not run.
        StripeErrorMapping
            .ToError(StripeFailure("authentication_error"))
            .Should().Be(PaymentErrors.GatewayNotAuthenticated);
    }

    [Fact]
    public void AnUnrecognisedErrorType_IsPermanentRatherThanADecline()
    {
        StripeException exception = StripeFailure("some_future_stripe_error");

        // Not transient — nothing is retried on a guess — and not a decline either, because
        // reporting "your card was declined" for an error nobody has classified is a lie told to a
        // customer.
        StripeErrorMapping.IsTransient(exception).Should().BeFalse();
        StripeErrorMapping.ToError(exception).Should().NotBeOfType<PaymentDeclinedError>();
    }

    [Fact]
    public void AnExceptionWithNoStripeError_IsStillMapped()
    {
        var exception = new StripeException("no error body");

        StripeErrorMapping.ToError(exception).Code.Should().Be("Payments.GatewayRequestInvalid");
    }
}
