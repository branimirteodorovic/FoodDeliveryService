using FoodDeliveryService.Modules.Notifications.Domain.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;

/// <summary>
/// The typed payload for a notification. Each concrete model declares exactly the fields its template
/// needs and which <see cref="NotificationType"/> it renders — replacing the untyped token dictionary,
/// so a mistyped field is a compile error rather than a silent default. Passed in-process via MediatR
/// (never serialized onto the bus), so a polymorphic model is safe here.
/// </summary>
public interface INotificationModel
{
    NotificationType Type { get; }
}

public sealed record OrderConfirmationModel(
    string FirstName,
    Guid OrderId,
    decimal Subtotal) : INotificationModel
{
    public NotificationType Type => NotificationType.OrderConfirmation;
}

/// <summary>
/// The agent's reply as the email renders it. <paramref name="Preview"/> is already truncated by
/// Support — this module does not hold the full message and deliberately does not ask for it, so the
/// email links the customer back to the thread rather than reproducing it.
/// </summary>
public sealed record SupportTicketReplyModel(
    string FirstName,
    string TicketReference,
    string TicketSubject,
    string Preview) : INotificationModel
{
    public NotificationType Type => NotificationType.SupportTicketReply;
}

/// <summary>
/// The outcome of a refund request as the email renders it.
/// <para>
/// <paramref name="Approved"/> is a bool rather than two models because the two emails differ only
/// in a sentence: one template arm keeps the amount, the reference and the note formatted the same
/// way for both, which is what stops a declined-refund email from quietly drifting into a different
/// shape from the approved one.
/// </para>
/// <para>
/// Approved means an administrator agreed and the refund has been sent to the payment provider, not
/// that the money has landed. Feature 3.8 made the approval real (§10.3 retires the "no payments"
/// wording that used to be here); what it did not do is make settlement instant, so this email still
/// says the decision was made and <see cref="RefundSettledModel"/> is the one that says the money is
/// on its way.
/// </para>
/// </summary>
public sealed record RefundDecisionModel(
    string FirstName,
    string TicketReference,
    decimal Amount,
    bool Approved,
    string? DecisionNote) : INotificationModel
{
    public NotificationType Type => NotificationType.RefundDecision;
}

/// <summary>
/// The declined card as the email renders it — Feature 3.8 Milestone H, §10.4.
/// <para>
/// <paramref name="Reason"/> is one of Payments' six bounded reason codes, and the template turns it
/// into a sentence itself: Stripe's own message is unbounded free text written by a third party, and
/// forwarding it unread to a customer is not something this platform does. An unrecognised code
/// renders as the neutral arm rather than throwing — the email matters more than the precision.
/// </para>
/// <para>
/// <b>There is no amount.</b> <c>PaymentAuthorizationFailedIntegrationEvent</c> does not carry one,
/// and that is correct rather than an omission to work around: nothing was charged, so the only
/// figure this module could print is one it invented or read out of another service's data. The
/// order reference is what the customer needs to find the order that did not happen.
/// </para>
/// </summary>
public sealed record PaymentFailedModel(
    string FirstName,
    Guid OrderId,
    string Reason) : INotificationModel
{
    public NotificationType Type => NotificationType.PaymentFailed;
}

/// <summary>
/// The refund that actually happened — Feature 3.8 Milestone H.
/// <para>
/// Separate from <see cref="RefundDecisionModel"/> because it answers a different question, and the
/// customer asks both: whether anyone agreed, and whether the money has gone. The one thing this
/// copy must not do is promise a date — a card refund clears at the issuer's pace, not the
/// platform's — so it says the refund has been sent and gives the usual window as a range.
/// </para>
/// </summary>
public sealed record RefundSettledModel(
    string FirstName,
    string TicketReference,
    decimal Amount) : INotificationModel
{
    public NotificationType Type => NotificationType.RefundSettled;
}
