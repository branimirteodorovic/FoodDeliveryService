using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Webhooks;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks;

/// <summary>
/// How a recorded <c>payment_intent.*</c> event finds the order it is about — shared by every arm
/// that mutates a <see cref="Payment"/>, because all of them must agree on the lock key before any
/// of them reads anything (Feature 3.8 Milestone F, §8.1).
/// </summary>
internal static class WebhookPaymentLookup
{
    /// <summary>
    /// Resolves the order id a provider event concerns, in two steps for one reason.
    /// <para>
    /// <b>The metadata first.</b> <c>AuthorizeAsync</c> writes this platform's <c>order_id</c> onto
    /// the PaymentIntent and Stripe echoes it back on every event about that intent, so the common
    /// path needs no database read at all — and, crucially, it still works when the <c>pi_…</c> was
    /// never recorded locally. That is the exact case worth reconciling: the provider answered, the
    /// process died before the answer was saved, and a lookup by <c>pi_…</c> would find nothing.
    /// </para>
    /// <para>
    /// <b>The local index second</b>, for an intent created before that metadata existed or by
    /// something else. It reads an id rather than an entity on purpose — see
    /// <see cref="IPaymentRepository.FindOrderIdByPaymentIntentIdAsync"/>.
    /// </para>
    /// </summary>
    public static async Task<Result<Guid>> ResolveOrderIdAsync(
        StripeEventLog eventLog,
        IPaymentRepository paymentRepository,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(eventLog.OrderReference, out Guid orderId))
        {
            return orderId;
        }

        if (string.IsNullOrWhiteSpace(eventLog.ObjectId))
        {
            return Result.Failure<Guid>(
                StripeEventLogErrors.EventIncomplete(eventLog.EventType, "payment intent"));
        }

        Guid? resolved = await paymentRepository.FindOrderIdByPaymentIntentIdAsync(
            eventLog.ObjectId,
            cancellationToken);

        return resolved is null
            ? Result.Failure<Guid>(PaymentErrors.NotFound($"provider intent {eventLog.ObjectId}"))
            : resolved.Value;
    }
}
