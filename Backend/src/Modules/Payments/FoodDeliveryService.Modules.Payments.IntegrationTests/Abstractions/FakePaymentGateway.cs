using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using ApplicationException = FoodDeliveryService.Common.Application.Exceptions.ApplicationException;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;

/// <summary>
/// The scriptable stand-in for Stripe — Feature 3.8 Milestone C, §5.5. Registered in place of
/// <see cref="IPaymentGateway"/> by the integration-test host, so the whole payment flow can be
/// driven end to end without a network, an API key, or a card that behaves differently on a Tuesday.
/// <para>
/// Two properties earn their place here and neither is "returns a canned value":
/// </para>
/// <list type="number">
/// <item>
/// <b>It records every call with its idempotency key.</b> That recording is what §13.2's
/// double-charge regression test asserts against — force an outbox retry, then check the gateway saw
/// the authorization once. Without the recording that test can only assert the payment's final
/// state, which is identical whether the card was charged once or twice.
/// </item>
/// <item>
/// <b>It honours idempotency keys the way Stripe does.</b> A second call with a key it has already
/// seen replays the first response instead of producing a new <c>pi_…</c>. A fake that minted a
/// fresh id per call would make a retried handler look like it created a second payment even when
/// the production code is correct — and, worse, would let genuinely missing keys pass unnoticed.
/// </item>
/// </list>
/// <para>
/// Deterministic throughout: identifiers are derived from the idempotency key, so the same test run
/// twice produces the same ids and a failure message is the same string both times.
/// </para>
/// </summary>
internal sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly Lock _gate = new();
    private readonly List<FakeGatewayCall> _calls = [];
    private readonly Dictionary<FakeGatewayOperation, FakeGatewayOutcome> _scripted = [];
    private readonly Dictionary<string, object> _responsesByIdempotencyKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _authorizedMinorUnits = new(StringComparer.Ordinal);

    /// <summary>What every operation does unless <see cref="Script"/> says otherwise.</summary>
    public FakeGatewayOutcome DefaultOutcome { get; set; } = FakeGatewayOutcome.Succeed;

    /// <summary>
    /// The reason attached to a <see cref="FakeGatewayOutcome.Decline"/>. One of the
    /// <see cref="PaymentFailureReason"/> constants — the fake will not emit a reason the real
    /// gateway could not, because a test that asserts on an impossible reason proves nothing.
    /// </summary>
    public string DeclineReason { get; set; } = PaymentFailureReason.CardDeclined;

    /// <summary>Every call, in order, including replays. See the class remarks.</summary>
    public IReadOnlyList<FakeGatewayCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public void Script(FakeGatewayOperation operation, FakeGatewayOutcome outcome)
    {
        lock (_gate)
        {
            _scripted[operation] = outcome;
        }
    }

    public IReadOnlyList<FakeGatewayCall> CallsFor(FakeGatewayOperation operation) =>
        [.. Calls.Where(call => call.Operation == operation)];

    public void Reset()
    {
        lock (_gate)
        {
            _calls.Clear();
            _scripted.Clear();
            _responsesByIdempotencyKey.Clear();
            _authorizedMinorUnits.Clear();
        }
    }

    public Task<Result<GatewayCustomer>> CreateCustomerAsync(
        Guid userId,
        string email,
        string? name,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Execute(
            FakeGatewayOperation.CreateCustomer,
            idempotencyKey,
            subject: userId.ToString(),
            amountMinorUnits: null,
            () => new GatewayCustomer(Identifier("cus", idempotencyKey))));

    public Task<Result<GatewaySetupIntent>> CreateSetupIntentAsync(
        string stripeCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Execute(
            FakeGatewayOperation.CreateSetupIntent,
            idempotencyKey,
            subject: stripeCustomerId,
            amountMinorUnits: null,
            () =>
            {
                string id = Identifier("seti", idempotencyKey);

                return new GatewaySetupIntent(id, $"{id}_secret_{Identifier("sec", idempotencyKey)}");
            }));

    public Task<Result<GatewayPaymentIntent>> AuthorizeAsync(
        GatewayAuthorization authorization,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        long minorUnits = authorization.Amount.ToMinorUnits();

        return Task.FromResult(Execute(
            FakeGatewayOperation.Authorize,
            idempotencyKey,
            subject: authorization.OrderId.ToString(),
            minorUnits,
            () =>
            {
                string paymentIntentId = Identifier("pi", idempotencyKey);
                _authorizedMinorUnits[paymentIntentId] = minorUnits;

                // requires_capture, never succeeded: this is a hold, and a fake that reported
                // "succeeded" here would let a §9 bug that skips the capture pass every test.
                return new GatewayPaymentIntent(
                    paymentIntentId,
                    GatewayPaymentIntentStatus.RequiresCapture,
                    minorUnits);
            }));
    }

    public Task<Result<GatewayPaymentIntent>> CaptureAsync(
        string paymentIntentId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Execute(
            FakeGatewayOperation.Capture,
            idempotencyKey,
            subject: paymentIntentId,
            AuthorizedMinorUnits(paymentIntentId),
            () => new GatewayPaymentIntent(
                paymentIntentId,
                GatewayPaymentIntentStatus.Succeeded,
                AuthorizedMinorUnits(paymentIntentId))));

    public Task<Result<GatewayPaymentIntent>> ReleaseAsync(
        string paymentIntentId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Execute(
            FakeGatewayOperation.Release,
            idempotencyKey,
            subject: paymentIntentId,
            AuthorizedMinorUnits(paymentIntentId),
            () => new GatewayPaymentIntent(
                paymentIntentId,
                GatewayPaymentIntentStatus.Canceled,
                AuthorizedMinorUnits(paymentIntentId))));

    public Task<Result<GatewayRefund>> RefundAsync(
        string paymentIntentId,
        Money amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(amount);

        return Task.FromResult(Execute(
            FakeGatewayOperation.Refund,
            idempotencyKey,
            subject: paymentIntentId,
            amount.ToMinorUnits(),
            () => new GatewayRefund(
                Identifier("re", idempotencyKey),
                GatewayRefundStatus.Succeeded,
                amount.ToMinorUnits())));
    }

    private Result<TResult> Execute<TResult>(
        FakeGatewayOperation operation,
        string idempotencyKey,
        string subject,
        long? amountMinorUnits,
        Func<TResult> succeed)
        where TResult : class
    {
        // The same guard the real gateway applies. A test that forgets the key must fail here rather
        // than pass and leave the production path unprotected.
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        lock (_gate)
        {
            FakeGatewayOutcome outcome = _scripted.TryGetValue(operation, out FakeGatewayOutcome scripted)
                ? scripted
                : DefaultOutcome;

            bool replayed = _responsesByIdempotencyKey.ContainsKey(idempotencyKey);

            _calls.Add(new FakeGatewayCall(operation, idempotencyKey, subject, amountMinorUnits, replayed));

            switch (outcome)
            {
                case FakeGatewayOutcome.ThrowTransient:
                    // What StripePaymentGateway does for an api_connection_error or a 5xx. Recorded
                    // before it is thrown, so a test can still see the attempt.
                    throw new ApplicationException(operation.ToString());

                case FakeGatewayOutcome.Decline:
                    return Result.Failure<TResult>(PaymentErrors.Declined(DeclineReason));

                case FakeGatewayOutcome.RequireAction:
                    // The real gateway sets ErrorOnRequiresAction, so an off-session 3-D Secure card
                    // never returns a requires_action status — it comes back as this decline (§8.5).
                    return Result.Failure<TResult>(
                        PaymentErrors.Declined(PaymentFailureReason.AuthenticationRequired));

                default:
                    if (replayed)
                    {
                        return Result.Success((TResult)_responsesByIdempotencyKey[idempotencyKey]);
                    }

                    TResult response = succeed();
                    _responsesByIdempotencyKey[idempotencyKey] = response;

                    return Result.Success(response);
            }
        }
    }

    private long AuthorizedMinorUnits(string paymentIntentId)
    {
        lock (_gate)
        {
            return _authorizedMinorUnits.TryGetValue(paymentIntentId, out long amount) ? amount : 0L;
        }
    }

    /// <summary>
    /// A Stripe-shaped identifier derived from the idempotency key. Deterministic on purpose: a
    /// failing assertion quotes the same id on every run, which makes it greppable across a test log
    /// and a database dump.
    /// </summary>
    private static string Identifier(string prefix, string idempotencyKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{prefix}:{idempotencyKey}"));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}_test_{Convert.ToHexString(hash)[..24].ToLowerInvariant()}");
    }
}

/// <summary>The six calls <see cref="IPaymentGateway"/> makes.</summary>
internal enum FakeGatewayOperation
{
    CreateCustomer,
    CreateSetupIntent,
    Authorize,
    Capture,
    Release,
    Refund
}

/// <summary>
/// The four behaviours §5.5 asks the fake to be able to produce, each corresponding to something the
/// real gateway does: a success, a card decline, an off-session 3-D Secure challenge, and a
/// transient provider fault.
/// </summary>
internal enum FakeGatewayOutcome
{
    Succeed,
    Decline,
    RequireAction,
    ThrowTransient
}

/// <param name="Subject">The order, customer or payment intent the call was about — for readable assertions.</param>
/// <param name="Replayed">
/// True when the idempotency key had already been used, so no new effect was produced. A retry that
/// reaches the gateway is not automatically a defect; a retry that produces a <i>second</i> charge
/// is, and this is the flag that tells the two apart.
/// </param>
internal sealed record FakeGatewayCall(
    FakeGatewayOperation Operation,
    string IdempotencyKey,
    string Subject,
    long? AmountMinorUnits,
    bool Replayed);
