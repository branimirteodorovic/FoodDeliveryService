using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.ReleasePayment;

/// <summary>
/// The order is not going ahead, so the hold is given up uncharged — Feature 3.8 Milestone G, §9.
/// The mirror of <c>CapturePaymentCommandHandler</c>: same lock on the same key, same tolerance for
/// a cash order having no row at all.
/// <para>
/// <b>Two events drive it and neither is preferred.</b> A rejection and a cancellation are different
/// stories about the order and the same instruction about the money, so they share one command
/// rather than each getting its own — and because the aggregate is a no-op from anything but
/// <see cref="PaymentStatus.Authorized"/>, an order that manages to produce both still releases once.
/// </para>
/// </summary>
internal sealed class ReleasePaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentGateway paymentGateway,
    IDistributedLock distributedLock,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    ILogger<ReleasePaymentCommandHandler> logger) : ICommandHandler<ReleasePaymentCommand>
{
    public async Task<Result> Handle(ReleasePaymentCommand request, CancellationToken cancellationToken)
    {
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
            // A cash order, or a card order whose placement never reached this service. Nothing is
            // held either way.
            return Result.Success();
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            // Captured already (a cancellation racing an acceptance), failed, or still authorizing.
            // The last of those is the one with a consequence — see §9.1 and Payment.Release.
            logger.LogInformation(
                "Order {OrderId}'s payment is {PaymentStatus}, so there is no hold to release",
                request.OrderId,
                payment.Status);

            return Result.Success();
        }

        if (payment.StripePaymentIntentId is null)
        {
            return Result.Failure(PaymentErrors.NotFound($"provider intent for order {request.OrderId}"));
        }

        Result<GatewayPaymentIntent> released = await paymentGateway.ReleaseAsync(
            payment.StripePaymentIntentId,
            PaymentIdempotencyKeys.Release(request.OrderId),
            cancellationToken);

        if (released.IsFailure)
        {
            // Left on the inbox row like a failed capture. The consequence is milder — an
            // uncaptured authorization expires at the issuer within a week — but a customer looking
            // at a held balance for that week is entitled to have somebody know about it.
            return Result.Failure(released.Error);
        }

        if (released.Value.Status != GatewayPaymentIntentStatus.Canceled)
        {
            logger.LogError(
                "Stripe returned {PaymentIntentStatus} for order {OrderId}'s release instead of canceled; " +
                "intent {PaymentIntentId} was not recorded as released",
                released.Value.Status,
                request.OrderId,
                released.Value.PaymentIntentId);

            return Result.Failure(PaymentErrors.ReleaseNotConfirmed(request.OrderId));
        }

        Result result = payment.Release(dateTimeProvider.UtcNow);

        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
