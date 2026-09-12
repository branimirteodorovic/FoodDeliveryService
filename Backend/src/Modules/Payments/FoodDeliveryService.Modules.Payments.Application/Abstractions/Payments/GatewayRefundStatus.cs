namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>Mapped off Stripe's <c>Refund.status</c>. See <see cref="GatewayPaymentIntentStatus"/>.</summary>
public enum GatewayRefundStatus
{
    Unknown = 0,

    /// <summary>
    /// Accepted and in flight. Card refunds are usually <see cref="Succeeded"/> immediately, but a
    /// pending refund is a normal outcome and §10 must not read it as a failure.
    /// </summary>
    Pending = 1,
    RequiresAction = 2,
    Succeeded = 3,
    Failed = 4,
    Canceled = 5
}
