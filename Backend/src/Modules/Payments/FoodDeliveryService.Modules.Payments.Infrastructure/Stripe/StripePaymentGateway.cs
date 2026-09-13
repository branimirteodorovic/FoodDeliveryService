using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain;
using Microsoft.Extensions.Logging;
using global::Stripe;
using ApplicationException = FoodDeliveryService.Common.Application.Exceptions.ApplicationException;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;

/// <summary>
/// The one implementation of <see cref="IPaymentGateway"/> — §5.3. Everything Stripe-shaped stops
/// here: the SDK, the API key, the decline codes and the status strings.
/// <para>
/// <b>The namespace of this file collides with the SDK's.</b> Inside
/// <c>…Infrastructure.Stripe</c>, a plain <c>using Stripe;</c> binds to the enclosing namespace and
/// every SDK type stops resolving. <c>using global::Stripe;</c> is the fix, and it is needed in
/// every file in this folder that touches the SDK — the same shape as the <c>Delivery</c> class
/// versus the Delivery module namespace.
/// </para>
/// </summary>
internal sealed class StripePaymentGateway(
    IStripeClient client,
    ILogger<StripePaymentGateway> logger) : IPaymentGateway
{
    private readonly CustomerService _customers = new(client);
    private readonly SetupIntentService _setupIntents = new(client);
    private readonly PaymentMethodService _paymentMethods = new(client);
    private readonly PaymentIntentService _paymentIntents = new(client);
    private readonly RefundService _refunds = new(client);

    public Task<Result<GatewayCustomer>> CreateCustomerAsync(
        Guid userId,
        string email,
        string? name,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var options = new CustomerCreateOptions
        {
            Email = email,
            Name = name,
            // The platform's own id, so a customer in the Stripe dashboard is traceable back to a
            // user without a lookup table on the side.
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["user_id"] = userId.ToString()
            }
        };

        return InvokeAsync(
            "create_customer",
            idempotencyKey,
            (requestOptions, ct) => _customers.CreateAsync(options, requestOptions, ct),
            customer => new GatewayCustomer(customer.Id),
            cancellationToken);
    }

    public Task<Result<GatewaySetupIntent>> CreateSetupIntentAsync(
        string stripeCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var options = new SetupIntentCreateOptions
        {
            Customer = stripeCustomerId,
            PaymentMethodTypes = ["card"],
            // off_session: this card is being saved now in order to be charged later without the
            // customer present, which is exactly what authorize-on-placement does. Declaring it at
            // setup time is what makes the later off-session charge acceptable to the issuer rather
            // than a 3-D Secure challenge nobody is there to answer.
            Usage = "off_session"
        };

        return InvokeAsync(
            "create_setup_intent",
            idempotencyKey,
            (requestOptions, ct) => _setupIntents.CreateAsync(options, requestOptions, ct),
            setupIntent => new GatewaySetupIntent(setupIntent.Id, setupIntent.ClientSecret),
            cancellationToken);
    }

    public Task<Result<GatewayPaymentMethod>> AttachPaymentMethodAsync(
        string stripeCustomerId,
        string paymentMethodId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var options = new PaymentMethodAttachOptions { Customer = stripeCustomerId };

        return InvokeAsync(
            "attach_payment_method",
            idempotencyKey,
            (requestOptions, ct) => _paymentMethods.AttachAsync(paymentMethodId, options, requestOptions, ct),
            ToGatewayPaymentMethod,
            cancellationToken);
    }

    public async Task<Result> DetachPaymentMethodAsync(
        string paymentMethodId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var options = new PaymentMethodDetachOptions();

        // Detach returns the payment method, but nothing here has any use for it: the card is being
        // forgotten. Mapped to a bare Result so no caller is tempted to read display fields off a
        // card that no longer exists.
        Result<GatewayPaymentMethod> result = await InvokeAsync(
            "detach_payment_method",
            idempotencyKey,
            (requestOptions, ct) => _paymentMethods.DetachAsync(paymentMethodId, options, requestOptions, ct),
            ToGatewayPaymentMethod,
            cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    public Task<Result<GatewayPaymentIntent>> AuthorizeAsync(
        GatewayAuthorization authorization,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var options = new PaymentIntentCreateOptions
        {
            Amount = authorization.Amount.ToMinorUnits(),
            // Stripe wants a lower-case currency code; Money holds the ISO 4217 upper-case one.
            Currency = authorization.Amount.Currency.ToLowerInvariant(),
            Customer = authorization.StripeCustomerId,
            PaymentMethod = authorization.PaymentMethodId,
            // The three options that make this an authorization rather than a charge: hold the
            // funds (manual capture), do it now rather than waiting for a browser (confirm), and
            // tell the issuer the customer is not present (off_session).
            CaptureMethod = "manual",
            Confirm = true,
            OffSession = true,
            // Without this, a card that wants 3-D Secure comes back with status requires_action and
            // leaves an intent hanging until it expires. There is no frontend to answer the
            // challenge (§8.5), so the honest outcome is a failure now — and this routes it through
            // the same card_error channel as any other decline, with code authentication_required.
            ErrorOnRequiresAction = true,
            Description = $"Order {authorization.OrderId}",
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["order_id"] = authorization.OrderId.ToString()
            }
        };

        return InvokeAsync(
            "authorize",
            idempotencyKey,
            (requestOptions, ct) => _paymentIntents.CreateAsync(options, requestOptions, ct),
            ToGatewayPaymentIntent,
            cancellationToken);
    }

    public Task<Result<GatewayPaymentIntent>> CaptureAsync(
        string paymentIntentId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // No AmountToCapture: capture the full authorized amount. Partial capture is out of scope,
        // and omitting the parameter is how Stripe says "all of it" — passing the amount we believe
        // we authorized would introduce a second source of truth for it.
        var options = new PaymentIntentCaptureOptions();

        return InvokeAsync(
            "capture",
            idempotencyKey,
            (requestOptions, ct) => _paymentIntents.CaptureAsync(paymentIntentId, options, requestOptions, ct),
            ToGatewayPaymentIntent,
            cancellationToken);
    }

    public Task<Result<GatewayPaymentIntent>> ReleaseAsync(
        string paymentIntentId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var options = new PaymentIntentCancelOptions
        {
            // Stripe's enumerated reasons are duplicate / fraudulent / requested_by_customer /
            // abandoned. Both paths that reach here — the restaurant rejected the order, or the
            // customer cancelled it — are the order not going ahead; <c>abandoned</c> would claim
            // the intent was simply never finished, which is a different story to tell an issuer.
            CancellationReason = "requested_by_customer"
        };

        return InvokeAsync(
            "release",
            idempotencyKey,
            (requestOptions, ct) => _paymentIntents.CancelAsync(paymentIntentId, options, requestOptions, ct),
            ToGatewayPaymentIntent,
            cancellationToken);
    }

    public Task<Result<GatewayRefund>> RefundAsync(
        string paymentIntentId,
        Money amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(amount);

        var options = new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId,
            Amount = amount.ToMinorUnits(),
            Reason = "requested_by_customer"
        };

        return InvokeAsync(
            "refund",
            idempotencyKey,
            (requestOptions, ct) => _refunds.CreateAsync(options, requestOptions, ct),
            refund => new GatewayRefund(
                refund.Id,
                StripeStatusMapping.Refund(refund.Status),
                refund.Amount),
            cancellationToken);
    }

    /// <summary>
    /// The four display fields, and nothing else off a Stripe <c>PaymentMethod</c> — §0.5. The
    /// <c>Card</c> property is null for every non-card type, and the SetupIntent asks for cards
    /// only, so a null here is a fact to carry rather than a reason to throw.
    /// </summary>
    private static GatewayPaymentMethod ToGatewayPaymentMethod(PaymentMethod paymentMethod) => new(
        paymentMethod.Id,
        paymentMethod.Card?.Brand,
        paymentMethod.Card?.Last4,
        (int?)paymentMethod.Card?.ExpMonth,
        (int?)paymentMethod.Card?.ExpYear);

    /// <summary>
    /// Amount is what was authorized; AmountReceived is what was actually taken, and it stays zero
    /// until a capture succeeds. Reporting the right one per status is what keeps §10's "refund no
    /// more than was captured" check honest.
    /// </summary>
    private static GatewayPaymentIntent ToGatewayPaymentIntent(PaymentIntent paymentIntent) => new(
        paymentIntent.Id,
        StripeStatusMapping.PaymentIntent(paymentIntent.Status),
        paymentIntent.Status == "succeeded" ? paymentIntent.AmountReceived : paymentIntent.Amount);

    /// <summary>
    /// The one place a Stripe call is actually made, so the idempotency key, the error mapping and
    /// the logging cannot be forgotten on a method added later.
    /// </summary>
    private async Task<Result<TResult>> InvokeAsync<TStripeEntity, TResult>(
        string operation,
        string idempotencyKey,
        Func<RequestOptions, CancellationToken, Task<TStripeEntity>> call,
        Func<TStripeEntity, TResult> map,
        CancellationToken cancellationToken)
    {
        // A blank key is not "no key, with a warning" — Stripe ignores an empty Idempotency-Key
        // header, so the call has no replay protection at all. Making the parameter required (§5.2)
        // stops it being forgotten; this stops it being satisfied with a string that does nothing.
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };

        try
        {
            TStripeEntity entity = await call(requestOptions, cancellationToken);

            return Result.Success(map(entity));
        }
        catch (StripeException exception)
        {
            if (StripeErrorMapping.IsTransient(exception))
            {
                logger.LogError(
                    exception,
                    "Stripe {Operation} failed transiently (idempotency key {IdempotencyKey}, HTTP {HttpStatusCode})",
                    operation,
                    idempotencyKey,
                    exception.HttpStatusCode);

                // Thrown rather than returned, so the failure is recorded as a fault instead of
                // being mistaken for a decline. Note what this does NOT do: neither
                // ProcessOutboxJob nor ProcessInboxJob retries — both write the exception into the
                // message's error column and mark it processed. Recovery is the reconciling webhook
                // (§7), and §8/§9 must not assume otherwise.
                throw new ApplicationException(operation, innerException: exception);
            }

            var error = StripeErrorMapping.ToError(exception);

            logger.LogWarning(
                "Stripe {Operation} was refused: {ErrorCode} (idempotency key {IdempotencyKey}, " +
                "stripe type {StripeErrorType}, stripe code {StripeErrorCode}, decline code {StripeDeclineCode})",
                operation,
                error.Code,
                idempotencyKey,
                exception.StripeError?.Type,
                exception.StripeError?.Code,
                exception.StripeError?.DeclineCode);

            return Result.Failure<TResult>(error);
        }
    }
}
