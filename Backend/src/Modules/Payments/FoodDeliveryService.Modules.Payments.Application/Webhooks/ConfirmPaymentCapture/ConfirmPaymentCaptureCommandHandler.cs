using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.ConfirmPaymentCapture;

/// <summary>
/// Stripe says an intent has been captured. Usually this platform already knew — and, as with its
/// two siblings, the arm exists for the times it did not (§7.7).
/// <para>
/// It makes <b>no provider call</b>. The event is the provider's own account of a capture that has
/// already happened, so the only work is recording it; calling <c>CaptureAsync</c> here would ask
/// Stripe to capture an intent it has just finished capturing.
/// </para>
/// <para>
/// <b>It can carry a payment two steps, not one.</b> A row still in
/// <see cref="PaymentStatus.Authorizing"/> whose intent Stripe reports as captured means both
/// responses were lost, not one, so the authorization is recorded before the capture — both
/// transitions raise their events and Orders converges through the ordinary projections rather than
/// jumping a state it never saw.
/// </para>
/// </summary>
internal sealed class ConfirmPaymentCaptureCommandHandler(
    IStripeEventLogRepository eventLogRepository,
    IPaymentRepository paymentRepository,
    IDistributedLock distributedLock,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork) : ICommandHandler<ConfirmPaymentCaptureCommand>
{
    public async Task<Result> Handle(
        ConfirmPaymentCaptureCommand request,
        CancellationToken cancellationToken)
    {
        StripeEventLog? eventLog = await eventLogRepository.GetAsync(request.EventLogId, cancellationToken);

        if (eventLog is null)
        {
            return Result.Failure(StripeEventLogErrors.NotFound(request.EventLogId));
        }

        if (eventLog.IsProcessed)
        {
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(eventLog.ObjectId))
        {
            return Result.Failure(StripeEventLogErrors.EventIncomplete(eventLog.EventType, "payment intent"));
        }

        Result<Guid> orderIdResult = await WebhookPaymentLookup.ResolveOrderIdAsync(
            eventLog,
            paymentRepository,
            cancellationToken);

        if (orderIdResult.IsFailure)
        {
            return Result.Failure(orderIdResult.Error);
        }

        await using IAsyncDisposable? handle = await distributedLock.TryAcquireAsync(
            PaymentLocks.Payment(orderIdResult.Value),
            PaymentLocks.Ttl,
            cancellationToken);

        if (handle is null)
        {
            // Almost always the capturing handler holding it, about to record exactly what this
            // event says. A failure rather than a success all the same: the retry on this path is
            // Stripe's own redelivery, and it only happens if the platform says something went wrong.
            return Result.Failure(PaymentErrors.AuthorizationInProgress(orderIdResult.Value));
        }

        Payment? payment = await paymentRepository.GetByOrderIdAsync(orderIdResult.Value, cancellationToken);

        if (payment is null)
        {
            return Result.Failure(PaymentErrors.NotFound($"order {orderIdResult.Value}"));
        }

        // Decide from the status ON THIS PAYLOAD (§7.5). A captured manual-capture intent is
        // `succeeded`; anything else on this event type is not a capture to record.
        if (!string.Equals(eventLog.ObjectStatus, PaymentIntentStatuses.Succeeded, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        if (payment.Status == PaymentStatus.Authorizing)
        {
            // Both responses were lost. Authorize first so the hold is recorded and published before
            // the charge that followed it — Payment.Authorize refuses a different intent, which is
            // still the guard that matters here.
            Result authorized = payment.Authorize(eventLog.ObjectId, dateTimeProvider.UtcNow);

            if (authorized.IsFailure)
            {
                return authorized;
            }
        }

        Result captured = payment.Capture(dateTimeProvider.UtcNow);

        if (captured.IsFailure)
        {
            return captured;
        }

        // Capture is a no-op from anything but Authorized, so this save is routinely a no-op too —
        // the correct shape for an at-least-once reconciliation.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
