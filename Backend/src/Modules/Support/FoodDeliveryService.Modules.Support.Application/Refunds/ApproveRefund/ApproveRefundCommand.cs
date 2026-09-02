using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.ApproveRefund;

/// <summary>
/// An administrator agrees to a refund. The deciding administrator is the authenticated caller and
/// is never a field here — segregation of duties would be worth nothing if the id it compares
/// against could be supplied by the person being compared.
/// </summary>
public sealed record ApproveRefundCommand(Guid RefundRequestId, string? Note) : ICommand;
