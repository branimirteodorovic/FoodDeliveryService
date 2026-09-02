using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Refunds;

public static class RefundErrors
{
    public static readonly Error AmountNotPositive = Error.Problem(
        "Refunds.AmountNotPositive",
        "A refund amount must be greater than zero");

    // The subtotal is read from the replicated order snapshot and handed to Create — the aggregate
    // never reaches for it. Refusing rather than clamping: an agent who typed 1000 instead of 10.00
    // should be told, not quietly granted the whole order.
    public static readonly Error AmountExceedsOrderSubtotal = Error.Problem(
        "Refunds.AmountExceedsOrderSubtotal",
        "A refund cannot exceed the subtotal of the order it refunds");

    public static readonly Error ReasonRequired = Error.Problem(
        "Refunds.ReasonRequired",
        "A refund request needs a reason");

    // A refund is always about an order. A ticket with no order on it ("the app crashes on
    // checkout") has nothing to refund, and inventing an order id to attach one to would make the
    // amount check meaningless.
    public static readonly Error TicketHasNoOrder = Error.Problem(
        "Refunds.TicketHasNoOrder",
        "A refund can only be requested on a ticket that names an order");

    // Support has not seen the order's OrderPlaced event, so there is no replicated subtotal to
    // validate against. Failing is the only honest answer: approving an unbounded amount because
    // the projection has not caught up is exactly the hole the subtotal check exists to close.
    public static Error OrderNotFound(Guid orderId) => Error.NotFound(
        "Refunds.OrderNotFound",
        $"No order with the identifier {orderId} is known to the support service");

    public static Error NotFound(Guid refundRequestId) => Error.NotFound(
        "Refunds.NotFound",
        $"The refund request with the identifier {refundRequestId} was not found");

    // Two agents working two tickets about the same order is a plausible race, and a second
    // approved refund on one order is real money in the world this record stands in for. Guarded
    // both by this check and by a unique partial index, because the check alone loses the race.
    public static readonly Error AlreadyRequestedForOrder = Error.Conflict(
        "Refunds.AlreadyRequestedForOrder",
        "This order already has a refund request awaiting a decision or already approved");

    // Approve/Reject are legal from Requested only. A second decision is refused rather than
    // overwriting the first: which administrator decided, and when, is the record.
    public static readonly Error AlreadyDecided = Error.Conflict(
        "Refunds.AlreadyDecided",
        "The refund request has already been decided");

    // Segregation of duties, enforced in the aggregate rather than as a policy checkbox: the whole
    // point of the approval step is that a second person looks at it.
    public static readonly Error RequesterCannotDecide = Error.Problem(
        "Refunds.RequesterCannotDecide",
        "The agent who requested a refund cannot decide it");

    // Lost the race for the decision lock. Retryable and strands nothing — the request is still in
    // the approval queue, so the next refresh either shows it decided or offers it again.
    public static readonly Error DecisionInProgress = Error.Problem(
        "Refunds.DecisionInProgress",
        "The refund request is being decided by another administrator — try again");
}
