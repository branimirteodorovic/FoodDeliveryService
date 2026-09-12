using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;

/// <summary>
/// Stripe's status strings, translated into this module's enums. One place, so a status is read the
/// same way whether it arrived on an API response or on a webhook payload (§7.5).
/// </summary>
internal static class StripeStatusMapping
{
    public static GatewayPaymentIntentStatus PaymentIntent(string? status) => status switch
    {
        "requires_payment_method" => GatewayPaymentIntentStatus.RequiresPaymentMethod,
        "requires_confirmation" => GatewayPaymentIntentStatus.RequiresConfirmation,
        "requires_action" => GatewayPaymentIntentStatus.RequiresAction,
        "processing" => GatewayPaymentIntentStatus.Processing,
        "requires_capture" => GatewayPaymentIntentStatus.RequiresCapture,
        "canceled" => GatewayPaymentIntentStatus.Canceled,
        "succeeded" => GatewayPaymentIntentStatus.Succeeded,
        // Unknown rather than a throw: a status Stripe adds later must not take the service down,
        // and Unknown is never treated as success anywhere.
        _ => GatewayPaymentIntentStatus.Unknown
    };

    public static GatewayRefundStatus Refund(string? status) => status switch
    {
        "pending" => GatewayRefundStatus.Pending,
        "requires_action" => GatewayRefundStatus.RequiresAction,
        "succeeded" => GatewayRefundStatus.Succeeded,
        "failed" => GatewayRefundStatus.Failed,
        "canceled" => GatewayRefundStatus.Canceled,
        _ => GatewayRefundStatus.Unknown
    };
}
