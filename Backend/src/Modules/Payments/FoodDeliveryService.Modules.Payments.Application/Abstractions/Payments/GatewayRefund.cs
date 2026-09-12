namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>A refund against a captured payment (§10).</summary>
public sealed record GatewayRefund(
    string RefundId,
    GatewayRefundStatus Status,
    long AmountMinorUnits);
