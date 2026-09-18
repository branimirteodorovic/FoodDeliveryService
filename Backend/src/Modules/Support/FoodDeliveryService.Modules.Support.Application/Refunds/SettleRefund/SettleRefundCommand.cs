using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.SettleRefund;

/// <summary>
/// Record what Payments did with an approved refund — Feature 3.8 Milestone H, §10.2.
/// <para>
/// One command for both outcomes, like <c>SendRefundDecisionCommand</c> before it and for the same
/// reason: the two are the same transition with a different verdict, and the work either side of
/// the verdict — find the request, guard the status, write the audit entry in the same transaction,
/// take the lock — is identical. Splitting them would be two handlers that differ by one line and
/// drift by more.
/// </para>
/// <para>
/// Driven only by the inbox, so it carries no validator — the scope <c>ValidatorCoverageTests</c>
/// deliberately excludes.
/// </para>
/// </summary>
/// <param name="FailureReason">
/// Payments' bounded reason code when <paramref name="Settled"/> is false, and null when it is true.
/// It is written onto the request and into the audit entry, which is where an agent reads it.
/// </param>
public sealed record SettleRefundCommand(
    Guid RefundRequestId,
    bool Settled,
    string? FailureReason) : ICommand;
