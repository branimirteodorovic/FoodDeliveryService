using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Refunds.RefundPayment;

/// <summary>
/// Give back money that was captured, for a refund an administrator has approved — Feature 3.8
/// Milestone H, §1.3 step 7.
/// <para>
/// Every field is copied from <c>RefundApprovedIntegrationEvent</c> and nothing is read back from
/// Support, which is hard rule #9 doing its job: the approval carries the ticket, its reference, the
/// order and the amount, so this service can act on it and tell the customer about it without ever
/// asking who approved what.
/// </para>
/// <para>
/// <see cref="RefundRequestId"/> is the durable id everything idempotent here is derived from — the
/// unique key of the <c>refunds</c> table and the Stripe idempotency key (rule 1 of §1.4).
/// </para>
/// <para>
/// Driven only by the inbox, so it carries no validator — the same rule as its siblings in
/// <c>AuthorizePayment</c> and <c>CapturePayment</c>, and the scope <c>ValidatorCoverageTests</c>
/// deliberately excludes.
/// </para>
/// </summary>
public sealed record RefundPaymentCommand(
    Guid RefundRequestId,
    Guid TicketId,
    string TicketReference,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount) : ICommand;
