using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.RejectRefund;

/// <summary>
/// An administrator declines a refund. Same shape as the approval, and gated on the same admin-only
/// permission: deciding is one authority, not two, and an account that could decline but not
/// approve would be a strange thing to grant.
/// </summary>
public sealed record RejectRefundCommand(Guid RefundRequestId, string? Note) : ICommand;
