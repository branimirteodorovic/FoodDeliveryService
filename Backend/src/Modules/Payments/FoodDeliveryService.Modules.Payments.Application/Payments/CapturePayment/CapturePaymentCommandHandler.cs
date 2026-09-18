using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.CapturePayment;

/// <summary>
/// The restaurant accepted, so the hold becomes a charge — Feature 3.8 Milestone G, §9.
/// <para>
/// The shape is <c>AuthorizePaymentCommandHandler</c>'s, minus the row creation: the lock comes
/// before the read (§1.4, rule 2), the provider call carries a key derived from the order (rule 1),
/// and the aggregate is what decides whether there is anything left to do.
/// </para>
/// <para>
/// <b>A missing payment is not an error here.</b> <c>OrderAcceptedIntegrationEvent</c> carries no
/// payment method — unlike <c>OrderPlaced</c>, which Milestone F extended precisely because that
/// handler had to skip cash orders before creating anything — so absence is how a cash order is
/// recognised on this path. Every card order has a row by the time it can be accepted, because
/// <c>Order.Accept()</c> refuses until the authorization has been projected back (§8.2). See §9.1.
/// </para>
/// <para>
/// <b>A failed capture IS an error</b>, in contrast to a failed authorization. A decline is the
/// card's answer to a question this platform asked, and recording it is handling it; a capture that
/// will not go through is money this platform said it would take from a real card and did not.
/// Nothing retries it (§5.6), so the most useful thing this handler can do is leave the reason on
/// the inbox row where an operator will find it.
/// </para>
/// </summary>
internal sealed class CapturePaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentGateway paymentGateway,
    IDistributedLock distributedLock,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    ILogger<CapturePaymentCommandHandler> logger) : ICommandHandler<CapturePaymentCommand>
{
    public async Task<Result> Handle(CapturePaymentCommand request, CancellationToken cancellationToken)
    {
        // Before the read, and on the key every other writer of this row uses — the webhook arms
        // included. See PaymentLocks.
        await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
            PaymentLocks.Payment(request.OrderId),
            PaymentLocks.Ttl,
            cancellationToken);

        if (handle is null)
        {
            return Result.Failure(PaymentErrors.AuthorizationInProgress(request.OrderId));
        }

        Payment? payment = await paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);

        if (payment is null)
        {
            // A cash order. There is nothing held and nothing to take.
            return Result.Success();
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            // Captured already by the reconciling webhook arm, released, or failed. The aggregate
            // would absorb the call anyway (§8.6); the point of checking here is to not make the
            // provider call at all — capturing a released intent is an error Stripe would return
            // and this handler would then have to explain away.
            logger.LogInformation(
                "Order {OrderId}'s payment is {PaymentStatus}, so there is nothing to capture",
                request.OrderId,
                payment.Status);

            return Result.Success();
        }

        if (payment.StripePaymentIntentId is null)
        {
            // Unreachable by construction — Payment.Authorize refuses without an intent id — and
            // checked rather than asserted because the alternative is a null reaching the SDK.
            return Result.Failure(PaymentErrors.NotFound($"provider intent for order {request.OrderId}"));
        }

        Result<GatewayPaymentIntent> captured = await paymentGateway.CaptureAsync(
            payment.StripePaymentIntentId,
            PaymentIdempotencyKeys.Capture(request.OrderId),
            cancellationToken);

        if (captured.IsFailure)
        {
            return Result.Failure(captured.Error);
        }

        if (captured.Value.Status != GatewayPaymentIntentStatus.Succeeded)
        {
            // A captured manual-capture intent is `succeeded` and nothing else. Recording a capture
            // off any other status would tell Orders, and eventually §10's refund ceiling, that
            // money moved when it may not have.
            logger.LogError(
                "Stripe returned {PaymentIntentStatus} for order {OrderId}'s capture instead of succeeded; " +
                "intent {PaymentIntentId} was not recorded as captured",
                captured.Value.Status,
                request.OrderId,
                captured.Value.PaymentIntentId);

            return Result.Failure(PaymentErrors.CaptureNotConfirmed(request.OrderId));
        }

        Result result = payment.Capture(dateTimeProvider.UtcNow);

        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
