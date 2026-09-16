using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Payments.IntegrationEvents;

/// <summary>
/// The order's card was not charged and will not be — Feature 3.8 Milestone F, §8.4.
/// <para>
/// Orders consumes it into <c>Order.FailPayment(...)</c>, which cancels the order and raises its own
/// distinct <c>OrderPaymentFailedDomainEvent</c> rather than reusing the customer's cancellation
/// (§8.3). Notifications is the second consumer (§10.4) and is the reason <see cref="Reason"/> is on
/// the contract at all: an email that says "your card was declined" and one that says "you cancelled
/// your order" are not the same email.
/// </para>
/// <para>
/// <b><see cref="Reason"/> is bounded</b> to the <c>PaymentFailureReason</c> constants —
/// <c>card_declined</c>, <c>insufficient_funds</c>, <c>expired_card</c>,
/// <c>authentication_required</c>, <c>gateway_error</c>, <c>no_payment_method</c> — and is never
/// Stripe's own message, which is unbounded free text written by a third party and would become one
/// metric time series per distinct wording (§11.1).
/// </para>
/// </summary>
public sealed class PaymentAuthorizationFailedIntegrationEvent : IntegrationEvent
{
    public PaymentAuthorizationFailedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid paymentId,
        Guid orderId,
        Guid customerId,
        string reason,
        DateTime failedOnUtc)
        : base(id, occurredOnUtc)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        CustomerId = customerId;
        Reason = reason;
        FailedOnUtc = failedOnUtc;
    }

    public Guid PaymentId { get; init; }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    /// <summary>One of the bounded reason codes — see the type remarks.</summary>
    public string Reason { get; init; }

    public DateTime FailedOnUtc { get; init; }
}
