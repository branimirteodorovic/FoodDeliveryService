using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using ApplicationException = FoodDeliveryService.Common.Application.Exceptions.ApplicationException;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;

/// <summary>
/// The test double's own tests. Ordinarily not worth writing — but §13.2's double-charge regression
/// test is an assertion <i>about</i> this class's recording, so if the recording is wrong the test
/// that matters most in this feature passes for the wrong reason.
/// <para>
/// These need no container, no Identity and no database. They run wherever the suite runs, which
/// today is a developer's machine rather than CI: the Payments integration suite joins the pipeline
/// with §6's first endpoint, when it starts needing the Identity host.
/// </para>
/// </summary>
public class FakePaymentGatewayTests
{
    private static readonly Guid OrderId = Guid.Parse("f2a0f3d6-3fc0-4a86-9f4e-0d2f2a1a63f1");

    // xUnit v3's xUnit1051 makes this mandatory rather than optional: an awaited call that ignores
    // the test's own cancellation token is a test the runner cannot stop.
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static GatewayAuthorization Authorization() => new(
        "cus_test",
        "pm_test",
        Money.Create(12.99m, "EUR").Value,
        OrderId);

    [Fact]
    public async Task EveryCall_IsRecordedWithItsIdempotencyKey()
    {
        var gateway = new FakePaymentGateway();
        string key = PaymentIdempotencyKeys.Authorize(OrderId);

        await gateway.AuthorizeAsync(Authorization(), key, Token);

        FakeGatewayCall call = gateway.Calls.Should().ContainSingle().Subject;
        call.Operation.Should().Be(FakeGatewayOperation.Authorize);
        call.IdempotencyKey.Should().Be(key);
        call.AmountMinorUnits.Should().Be(1299);
        call.Replayed.Should().BeFalse();
    }

    [Fact]
    public async Task ASecondCallWithTheSameKey_ReplaysTheFirstResponse()
    {
        // Stripe's actual behaviour, and the reason a retry is survivable. Both attempts are
        // recorded — the second is flagged as a replay — but only one payment exists.
        var gateway = new FakePaymentGateway();
        string key = PaymentIdempotencyKeys.Authorize(OrderId);

        Result<GatewayPaymentIntent> first = await gateway.AuthorizeAsync(Authorization(), key, Token);
        Result<GatewayPaymentIntent> second = await gateway.AuthorizeAsync(Authorization(), key, Token);

        second.Value.PaymentIntentId.Should().Be(first.Value.PaymentIntentId);
        gateway.Calls.Should().HaveCount(2);
        gateway.Calls[1].Replayed.Should().BeTrue();
    }

    [Fact]
    public async Task DifferentKeys_ProduceDifferentPayments()
    {
        var gateway = new FakePaymentGateway();

        Result<GatewayPaymentIntent> first = await gateway.AuthorizeAsync(
            Authorization(), PaymentIdempotencyKeys.Authorize(OrderId), Token);
        Result<GatewayPaymentIntent> second = await gateway.AuthorizeAsync(
            Authorization(), PaymentIdempotencyKeys.Authorize(Guid.NewGuid()), Token);

        // The double charge, in miniature. A production path that builds its key from anything that
        // varies between attempts lands exactly here.
        second.Value.PaymentIntentId.Should().NotBe(first.Value.PaymentIntentId);
    }

    [Fact]
    public async Task ABlankKey_IsRefused()
    {
        var gateway = new FakePaymentGateway();

        await Assert.ThrowsAsync<ArgumentException>(
            () => gateway.AuthorizeAsync(Authorization(), "   ", Token));
    }

    [Fact]
    public async Task AScriptedDecline_CarriesTheBoundedReason()
    {
        var gateway = new FakePaymentGateway { DeclineReason = PaymentFailureReason.InsufficientFunds };
        gateway.Script(FakeGatewayOperation.Authorize, FakeGatewayOutcome.Decline);

        Result<GatewayPaymentIntent> result = await gateway.AuthorizeAsync(
            Authorization(), PaymentIdempotencyKeys.Authorize(OrderId), Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<PaymentDeclinedError>()
            .Which.Reason.Should().Be(PaymentFailureReason.InsufficientFunds);
    }

    [Fact]
    public async Task AScriptedRequireAction_IsAnAuthenticationFailure()
    {
        var gateway = new FakePaymentGateway();
        gateway.Script(FakeGatewayOperation.Authorize, FakeGatewayOutcome.RequireAction);

        Result<GatewayPaymentIntent> result = await gateway.AuthorizeAsync(
            Authorization(), PaymentIdempotencyKeys.Authorize(OrderId), Token);

        result.Error.Should().BeOfType<PaymentDeclinedError>()
            .Which.Reason.Should().Be(PaymentFailureReason.AuthenticationRequired);
    }

    [Fact]
    public async Task AScriptedTransientFault_Throws()
    {
        var gateway = new FakePaymentGateway();
        gateway.Script(FakeGatewayOperation.Authorize, FakeGatewayOutcome.ThrowTransient);

        await Assert.ThrowsAsync<ApplicationException>(
            () => gateway.AuthorizeAsync(Authorization(), PaymentIdempotencyKeys.Authorize(OrderId), Token));

        // Recorded before it threw: a test asserting "the gateway was reached" must still be able to
        // see the attempt.
        gateway.CallsFor(FakeGatewayOperation.Authorize).Should().ContainSingle();
    }

    [Fact]
    public async Task ScriptingIsPerOperation()
    {
        var gateway = new FakePaymentGateway();
        gateway.Script(FakeGatewayOperation.Capture, FakeGatewayOutcome.Decline);

        Result<GatewayPaymentIntent> authorized = await gateway.AuthorizeAsync(
            Authorization(), PaymentIdempotencyKeys.Authorize(OrderId), Token);
        Result<GatewayPaymentIntent> captured = await gateway.CaptureAsync(
            authorized.Value.PaymentIntentId, PaymentIdempotencyKeys.Capture(OrderId), Token);

        authorized.IsSuccess.Should().BeTrue();
        captured.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AnAuthorizationIsAHoldAndACaptureIsTheCharge()
    {
        var gateway = new FakePaymentGateway();

        Result<GatewayPaymentIntent> authorized = await gateway.AuthorizeAsync(
            Authorization(), PaymentIdempotencyKeys.Authorize(OrderId), Token);
        Result<GatewayPaymentIntent> captured = await gateway.CaptureAsync(
            authorized.Value.PaymentIntentId, PaymentIdempotencyKeys.Capture(OrderId), Token);

        authorized.Value.Status.Should().Be(GatewayPaymentIntentStatus.RequiresCapture);
        captured.Value.Status.Should().Be(GatewayPaymentIntentStatus.Succeeded);
        captured.Value.AmountMinorUnits.Should().Be(1299);
    }

    [Fact]
    public async Task Identifiers_AreDeterministic()
    {
        Result<GatewayPaymentIntent> first = await new FakePaymentGateway()
            .AuthorizeAsync(Authorization(), PaymentIdempotencyKeys.Authorize(OrderId), Token);
        Result<GatewayPaymentIntent> second = await new FakePaymentGateway()
            .AuthorizeAsync(Authorization(), PaymentIdempotencyKeys.Authorize(OrderId), Token);

        second.Value.PaymentIntentId.Should().Be(first.Value.PaymentIntentId);
    }
}
