using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.ConfirmPaymentAuthorization;

/// <summary>
/// Stripe says an intent now has capturable funds on it. Usually this platform already knew — and
/// the whole value of the arm is the times it did not (§7.7).
/// <para>
/// <b>This is where rule 2 of §1.4 finally has two processes to separate.</b> Milestone E's own
/// webhook arm deliberately takes no lock, because it mutates a <c>CustomerPaymentProfile</c> whose
/// unique index already makes a redelivery a no-op. This one mutates a <see cref="Payment"/> that
/// <c>AuthorizePaymentCommandHandler</c> may be writing at this very moment, so it takes the lock,
/// before the read, on the same key that handler uses.
/// </para>
/// </summary>
internal sealed class ConfirmPaymentAuthorizationCommandHandler(
    IStripeEventLogRepository eventLogRepository,
    IPaymentRepository paymentRepository,
    IDistributedLock distributedLock,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork) : ICommandHandler<ConfirmPaymentAuthorizationCommand>
{
    public async Task<Result> Handle(
        ConfirmPaymentAuthorizationCommand request,
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
            // Almost always the authorizing handler holding it, which is about to record exactly
            // what this event says. Still a failure rather than a success: a lost acquisition must
            // land somewhere a retry exists, and the retry here is Stripe's own redelivery of the
            // event — which only happens if the platform says something went wrong.
            return Result.Failure(PaymentErrors.AuthorizationInProgress(orderIdResult.Value));
        }

        Payment? payment = await paymentRepository.GetByOrderIdAsync(orderIdResult.Value, cancellationToken);

        if (payment is null)
        {
            // An intent for an order this service has no payment for. Recorded on the event row
            // rather than thrown away, because the alternative explanation — another environment
            // pointed at this webhook endpoint — is one an operator needs to see.
            return Result.Failure(PaymentErrors.NotFound($"order {orderIdResult.Value}"));
        }

        // Decide from the status ON THIS PAYLOAD (§7.5), never from the order deliveries arrived in.
        // Stripe's requires_capture is what a held, uncaptured authorization looks like; anything
        // else on this event type is a payload this arm has no business acting on.
        if (!string.Equals(eventLog.ObjectStatus, PaymentIntentStatuses.RequiresCapture, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        Result authorized = payment.Authorize(eventLog.ObjectId, dateTimeProvider.UtcNow);

        if (authorized.IsFailure)
        {
            return authorized;
        }

        // Authorize is a no-op when the payment is already authorized or terminal, so this save is
        // routinely a no-op too — which is the correct shape for an at-least-once reconciliation.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
