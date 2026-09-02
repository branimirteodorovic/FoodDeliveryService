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
/// Approved means an administrator agreed, not that money moved — this platform processes no
/// payments — so the copy says the decision was made and never that funds are on their way.
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
