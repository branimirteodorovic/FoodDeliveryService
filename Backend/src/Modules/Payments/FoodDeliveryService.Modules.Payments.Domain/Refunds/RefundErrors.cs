using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Refunds;

/// <summary>
/// The <see cref="Refund"/> aggregate's own errors — Feature 3.8 Milestone H.
/// <para>
/// The refusals a customer's refund can actually meet are <em>not</em> here: a cash order, an
/// uncaptured payment and an over-large amount are outcomes that get recorded and published as
/// <see cref="RefundFailureReason"/> values, not errors that abort a handler. What is left here is
/// the small set that means the code is wrong.
/// </para>
/// </summary>
public static class RefundErrors
{
    public static readonly Error AmountNotPositive = Error.Problem(
        "Refunds.AmountNotPositive",
        "A refund amount must be greater than zero");

    /// <summary>
    /// Unreachable from Support, whose own aggregate copies the reference off the ticket at
    /// creation. Checked because the alternative is an email with an empty reference in the subject
    /// line, which is the one place this value is read by a person.
    /// </summary>
    public static readonly Error TicketReferenceRequired = Error.Problem(
        "Refunds.TicketReferenceRequired",
        "A refund needs the reference of the ticket it was raised on");

    public static readonly Error ProviderRefundRequired = Error.Problem(
        "Refunds.ProviderRefundRequired",
        "A provider refund identifier is required");

    /// <summary>
    /// A reason outside <see cref="RefundFailureReason"/> reached the aggregate. Checked rather than
    /// trusted for the same reason its payment-side counterpart is: the value ends up on a metric
    /// tag and in Support's audit log.
    /// </summary>
    public static Error FailureReasonUnknown(string reason) => Error.Problem(
        "Refunds.FailureReasonUnknown",
        $"'{reason}' is not a recognised refund failure reason");
}
