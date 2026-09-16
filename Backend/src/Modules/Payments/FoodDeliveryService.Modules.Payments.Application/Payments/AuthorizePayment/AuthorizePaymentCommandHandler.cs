using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.AuthorizePayment;

/// <summary>
/// Holds the order's money on the customer's saved card — the whole of §1.3 step 3.
/// <para>
/// <b>The write comes before the provider call, and the lock comes before the read.</b> Those two
/// orderings are the milestone. The row in <see cref="PaymentStatus.Authorizing"/> is what the
/// reconciling webhook has to find in order to finish a payment whose response was lost (§7.7); the
/// lock is rule 2 of §1.4, taken before the read because the check-then-act begins at the read and a
/// lock taken after it still lets two callers act on the same stale snapshot.
/// </para>
/// <para>
/// <b>A decline is a success.</b> The card being refused is a business outcome that this handler
/// records and publishes — it is not a handler failure, and returning one would put an error on the
/// inbox row for a payment that was handled exactly correctly. Only an outcome nobody has recorded
/// anywhere returns a failure.
/// </para>
/// </summary>
internal sealed class AuthorizePaymentCommandHandler(
    IPaymentRepository paymentRepository,
    ICustomerPaymentProfileRepository profileRepository,
    IPaymentGateway paymentGateway,
    IDistributedLock distributedLock,
    IOptions<PaymentsOptions> paymentsOptions,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    ILogger<AuthorizePaymentCommandHandler> logger) : ICommandHandler<AuthorizePaymentCommand>
{
    public async Task<Result> Handle(AuthorizePaymentCommand request, CancellationToken cancellationToken)
    {
        // Acquire BEFORE the read — the check-then-act begins at the read, so a lock taken after it
        // still lets both callers act on the same stale snapshot. The webhook arms and this handler
        // are two processes racing on this row, and no aggregate here carries a concurrency token.
        await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
            PaymentLocks.Payment(request.OrderId),
            PaymentLocks.Ttl,
            cancellationToken);

        if (handle is null)
        {
            // A failure, not a success: nothing retries this (§5.6), so returning success would
            // strand the payment silently. The reason lands on the inbox row, and the reconciling
            // webhook is what actually finishes it.
            return Result.Failure(PaymentErrors.AuthorizationInProgress(request.OrderId));
        }

        Payment? existing = await paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);

        if (existing is not null)
        {
            // The inbox dispatches at least once. One order has one payment, so a second delivery of
            // the placement has nothing left to do — and must not reach the gateway, where the
            // idempotency key would replay a 24-hour-old response rather than say so.
            return Result.Success();
        }

        CustomerPaymentProfile? profile = await profileRepository.GetAsync(
            request.CustomerId,
            cancellationToken);

        if (profile is null)
        {
            // No Stripe customer at all: UserRegistered has not been consumed for this person yet
            // (§6.2). There is nothing to record a failed payment against either, so this is the one
            // outcome here that is genuinely a handler failure.
            return Result.Failure(PaymentErrors.CustomerProfileNotFound(request.CustomerId));
        }

        Result<Money> amountResult = Money.Create(request.Subtotal, paymentsOptions.Value.Currency);

        if (amountResult.IsFailure)
        {
            return Result.Failure(amountResult.Error);
        }

        Result<Payment> paymentResult = Payment.Start(
            Guid.CreateVersion7(),
            request.OrderId,
            request.CustomerId,
            amountResult.Value,
            dateTimeProvider.UtcNow);

        if (paymentResult.IsFailure)
        {
            return Result.Failure(paymentResult.Error);
        }

        Payment payment = paymentResult.Value;

        paymentRepository.Insert(payment);

        // Committed on its own, before a single provider call. A payment Stripe knows about and this
        // platform does not is the only state in this feature with no way back: there is no id to
        // look up, nothing for the webhook to reconcile against, and a real hold sitting on a real
        // card. One extra round trip buys that away.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (profile.StripePaymentMethodId is null)
        {
            // Orders checks its CanPayByCard replica at placement, and the replica is allowed to be
            // a second out of date (§6.3) — this is that second. Recorded as an ordinary failed
            // payment with its own reason, so the order is cancelled the same way a decline would
            // cancel it and the count is not filed under "the provider misbehaved".
            logger.LogInformation(
                "Order {OrderId} was placed as a card order but customer {CustomerId} has no saved card",
                request.OrderId,
                request.CustomerId);

            return await FailAsync(
                payment,
                PaymentFailureReason.NoPaymentMethod,
                stripePaymentIntentId: null,
                cancellationToken);
        }

        Result<GatewayPaymentIntent> authorization = await paymentGateway.AuthorizeAsync(
            new GatewayAuthorization(
                profile.StripeCustomerId,
                profile.StripePaymentMethodId,
                amountResult.Value,
                request.OrderId),
            PaymentIdempotencyKeys.Authorize(request.OrderId),
            cancellationToken);

        if (authorization.IsFailure)
        {
            // The reason is recovered with a type test rather than by parsing the error code (§5.6):
            // PaymentDeclinedError carries the bounded reason as data. Anything that is not a
            // decline — a rejected request, a refused API key — is our own fault, not the card's.
            string reason = authorization.Error is PaymentDeclinedError declined
                ? declined.Reason
                : PaymentFailureReason.GatewayError;

            return await FailAsync(payment, reason, stripePaymentIntentId: null, cancellationToken);
        }

        GatewayPaymentIntent intent = authorization.Value;

        if (intent.Status != GatewayPaymentIntentStatus.RequiresCapture)
        {
            // A manual-capture intent that confirmed off-session can only land on requires_capture,
            // because the seam already turns an off-session 3-D Secure challenge into a decline
            // rather than a hanging intent — see §8.5. So anything else is a state this seam does
            // not model, and treating it as an authorization would let §9 try to capture a hold that
            // is not there. The provider's id is recorded even though the payment failed: it is what
            // an operator quotes in the Stripe dashboard.
            logger.LogError(
                "Stripe returned {PaymentIntentStatus} for order {OrderId}'s authorization instead of " +
                "requires_capture; intent {PaymentIntentId} was not treated as a hold",
                intent.Status,
                request.OrderId,
                intent.PaymentIntentId);

            return await FailAsync(
                payment,
                PaymentFailureReason.GatewayError,
                intent.PaymentIntentId,
                cancellationToken);
        }

        // The amount is deliberately not re-checked against what was sent. This service set it on
        // the create call and Stripe does not alter it, so an inequality here would be unreachable —
        // and the only honest response to one, refusing a hold that exists, would strand real money
        // on a real card with nothing left to release it.
        Result authorized = payment.Authorize(intent.PaymentIntentId, dateTimeProvider.UtcNow);

        if (authorized.IsFailure)
        {
            return authorized;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task<Result> FailAsync(
        Payment payment,
        string reason,
        string? stripePaymentIntentId,
        CancellationToken cancellationToken)
    {
        Result failed = payment.Fail(reason, stripePaymentIntentId, dateTimeProvider.UtcNow);

        if (failed.IsFailure)
        {
            return failed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Success: the failure is the outcome, and it has been recorded and will be published. The
        // handler did its job.
        return Result.Success();
    }
}
