using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.FailPaymentAuthorization;

/// <summary>
/// Records the refusal a <c>payment_intent.payment_failed</c> announced — Feature 3.8 Milestone F.
/// <para>
/// Takes the log row's identifier only, for the reason its sibling arm does: the intent and the
/// bounded failure reason are read from the row this platform wrote, not chosen by the caller. A
/// command that took a reason as a parameter would be a way to cancel somebody's order by asserting
/// that their card was declined.
/// </para>
/// </summary>
public sealed record FailPaymentAuthorizationCommand(Guid EventLogId) : ICommand;
