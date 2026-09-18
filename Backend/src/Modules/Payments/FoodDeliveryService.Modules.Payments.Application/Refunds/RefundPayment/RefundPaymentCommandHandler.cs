using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Refunds;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Modules.Payments.Application.Refunds.RefundPayment;

/// <summary>
/// An administrator approved a refund, so the money goes back — Feature 3.8 Milestone H, §10.1.
/// <para>
/// The shape is <c>AuthorizePaymentCommandHandler</c>'s, and deliberately so: the lock before the
/// read (§1.4 rule 2), the row written and committed before the provider is called, and the
/// idempotency key derived from a durable id (rule 1). It refunds against the same lock key every
/// other payment mutation takes, which is what stops a refund and a capture reconciled by a webhook
/// from reading the same status at the same instant.
/// </para>
/// <para>
/// <b>Every refusal is a success here, including the ones that refuse before calling Stripe.</b>
/// That is the opposite of <c>CapturePaymentCommandHandler</c> and the same as the authorization's,
/// and the dividing line is the same one §9.1 drew: a failure that has been recorded and published
/// has been handled, and only an outcome nobody learns about is a handler failure. A refund that
/// cannot happen — a cash order, an uncaptured payment, an amount above what was taken — is
/// something an agent is waiting to be told, and it reaches them as
/// <c>RefundFailedIntegrationEvent</c> and an entry in Support's audit log. Throwing instead would
/// put the reason on an inbox row nobody reads and leave Support's request saying "approved"
/// forever.
/// </para>
/// </summary>
internal sealed class RefundPaymentCommandHandler(
    IRefundRepository refundRepository,
    IPaymentRepository paymentRepository,
    IPaymentGateway paymentGateway,
    IDistributedLock distributedLock,
    IOptions<PaymentsOptions> paymentsOptions,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    ILogger<RefundPaymentCommandHandler> logger) : ICommandHandler<RefundPaymentCommand>
{
    public async Task<Result> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        // The order, not the refund: this contends with the capture and the webhook arms, and they
        // all serialize on PaymentLocks.Payment. A refund-scoped key would be a lock nobody else
        // takes, which is the same as no lock at all.
        await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
            PaymentLocks.Payment(request.OrderId),
            PaymentLocks.Ttl,
            cancellationToken);

        if (handle is null)
        {
            return Result.Failure(PaymentErrors.AuthorizationInProgress(request.OrderId));
        }

        Refund? existing = await refundRepository.GetByRefundRequestIdAsync(
            request.RefundRequestId,
            cancellationToken);

        if (existing is not null)
        {
            // A redelivered approval. One approved request refunds once, and the row is how that is
            // known — the Stripe idempotency key underneath would replay a 24-hour-old response
            // rather than say so, which is protection against a double refund but not a reason to
            // ask for one.
            return Result.Success();
        }

        Result<Money> amountResult = Money.Create(request.Amount, paymentsOptions.Value.Currency);

        if (amountResult.IsFailure)
        {
            return Result.Failure(amountResult.Error);
        }

        Money amount = amountResult.Value;

        Result<Refund> refundResult = Refund.Start(
            Guid.CreateVersion7(),
            request.RefundRequestId,
            request.TicketId,
            request.TicketReference,
            request.OrderId,
            request.CustomerId,
            amount,
            dateTimeProvider.UtcNow);

        if (refundResult.IsFailure)
        {
            return Result.Failure(refundResult.Error);
        }

        Refund refund = refundResult.Value;

        refundRepository.Insert(refund);

        // Committed before a single provider call, for the reason Payment.Start is: a refund Stripe
        // performed and this platform has no row for is money out of the business with nothing
        // pointing at it, and no id to look it up by.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        Payment? payment = await paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);

        if (payment is null)
        {
            // A cash order. Support lets a refund be requested on any order — its ceiling is the
            // replicated subtotal, which a cash order has like any other — so this is an ordinary
            // outcome rather than a surprise, and the approval still stands. It just has to be
            // settled in the world rather than by this service, and saying so is the whole point.
            logger.LogInformation(
                "Refund request {RefundRequestId} was approved for order {OrderId}, which has no card " +
                "payment; the refund has to be settled outside the platform",
                request.RefundRequestId,
                request.OrderId);

            return await FailAsync(refund, RefundFailureReason.NotCardPayment, cancellationToken);
        }

        decimal alreadyRefunded = await refundRepository.GetSettledTotalForOrderAsync(
            request.OrderId,
            cancellationToken);

        // Asked before the call, applied after it. The aggregate owns both halves — see
        // Payment.EnsureRefundable for why they are two methods rather than one.
        Result refundable = payment.EnsureRefundable(amount, alreadyRefunded);

        if (refundable.IsFailure)
        {
            return await FailAsync(refund, ToFailureReason(refundable.Error), cancellationToken);
        }

        if (payment.StripePaymentIntentId is null)
        {
            // Unreachable by construction — a captured payment has an intent — and checked rather
            // than asserted because the alternative is a null reaching the SDK.
            return await FailAsync(refund, RefundFailureReason.GatewayError, cancellationToken);
        }

        Result<GatewayRefund> refunded = await paymentGateway.RefundAsync(
            payment.StripePaymentIntentId,
            amount,
            PaymentIdempotencyKeys.Refund(request.RefundRequestId),
            cancellationToken);

        if (refunded.IsFailure)
        {
            logger.LogError(
                "Stripe refused the refund for order {OrderId} ({Error}); refund request " +
                "{RefundRequestId} was not settled",
                request.OrderId,
                refunded.Error.Code,
                request.RefundRequestId);

            return await FailAsync(refund, RefundFailureReason.GatewayError, cancellationToken);
        }

        if (refunded.Value.Status is not (GatewayRefundStatus.Succeeded or GatewayRefundStatus.Pending))
        {
            // Pending counts as accepted — see Refund.Settle. Anything else (failed, canceled,
            // requires_action) is a refund that is not going to happen on its own, and recording it
            // as settled would tell the customer their money is coming back when it is not.
            logger.LogError(
                "Stripe returned {RefundStatus} for order {OrderId}'s refund; refund {ProviderRefundId} " +
                "was not recorded as settled",
                refunded.Value.Status,
                request.OrderId,
                refunded.Value.RefundId);

            return await FailAsync(refund, RefundFailureReason.GatewayError, cancellationToken);
        }

        Result settled = refund.Settle(refunded.Value.RefundId, dateTimeProvider.UtcNow);

        if (settled.IsFailure)
        {
            return settled;
        }

        // Re-checked, not assumed: the same lock has been held throughout, so this cannot now refuse
        // — but the aggregate is the rule of record and a caller that skipped it would be the way
        // this ceiling quietly stops applying.
        Result applied = payment.Refund(amount, alreadyRefunded, dateTimeProvider.UtcNow);

        if (applied.IsFailure)
        {
            return applied;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// The aggregate's vocabulary mapped onto the bounded one that leaves this service. A type test
    /// is not available here the way <c>PaymentDeclinedError</c> allows on the authorization path,
    /// so it is the error code — which is a constant in <c>PaymentErrors</c>, not a message.
    /// </summary>
    private static string ToFailureReason(Error error) => error.Code switch
    {
        "Payments.NotCaptured" => RefundFailureReason.PaymentNotCaptured,
        "Payments.RefundExceedsCaptured" => RefundFailureReason.AmountExceedsCaptured,
        _ => RefundFailureReason.GatewayError
    };

    private async Task<Result> FailAsync(
        Refund refund,
        string reason,
        CancellationToken cancellationToken)
    {
        Result failed = refund.Fail(reason, dateTimeProvider.UtcNow);

        if (failed.IsFailure)
        {
            return failed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Success: the refusal is the outcome, and it has been recorded and will be published.
        return Result.Success();
    }
}
