using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.FailPaymentAuthorization;

/// <summary>
/// Stripe says the charge did not go through. The mirror of
/// <c>ConfirmPaymentAuthorizationCommandHandler</c>, and it takes the same lock on the same key for
/// the same reason (§1.4, rule 2).
/// <para>
/// Like its sibling it is usually redundant — the authorizing handler already saw the decline in the
/// call's own response — and, like its sibling, it is the only thing that finishes the payment when
/// that response never came back. <see cref="Payment.Fail"/> is a no-op from anything but
/// <see cref="PaymentStatus.Authorizing"/>, so a failure event about an authorized payment cannot
/// cancel an order whose money is already held.
/// </para>
/// </summary>
internal sealed class FailPaymentAuthorizationCommandHandler(
    IStripeEventLogRepository eventLogRepository,
    IPaymentRepository paymentRepository,
    IDistributedLock distributedLock,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork) : ICommandHandler<FailPaymentAuthorizationCommand>
{
    public async Task<Result> Handle(
        FailPaymentAuthorizationCommand request,
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
            return Result.Failure(PaymentErrors.AuthorizationInProgress(orderIdResult.Value));
        }

        Payment? payment = await paymentRepository.GetByOrderIdAsync(orderIdResult.Value, cancellationToken);

        if (payment is null)
        {
            return Result.Failure(PaymentErrors.NotFound($"order {orderIdResult.Value}"));
        }

        // Decide from the status ON THIS PAYLOAD (§7.5). A failure event is normally delivered with
        // the intent back at requires_payment_method — Stripe detaches the method that failed. The
        // guard is deliberately narrow: only a payload that says the funds ARE held is refused,
        // because that is the one contradiction with real consequences, and being any stricter would
        // discard a genuine refusal for arriving in a shape this arm did not predict.
        if (string.Equals(eventLog.ObjectStatus, PaymentIntentStatuses.RequiresCapture, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        // The reason was mapped onto the bounded set by the parser, where every other translation of
        // the provider's vocabulary happens. A payload that named no error at all still failed on
        // the card, so it counts as a plain decline rather than as something this platform did.
        string reason = eventLog.FailureReason ?? PaymentFailureReason.CardDeclined;

        Result failed = payment.Fail(reason, eventLog.ObjectId, dateTimeProvider.UtcNow);

        if (failed.IsFailure)
        {
            return failed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
