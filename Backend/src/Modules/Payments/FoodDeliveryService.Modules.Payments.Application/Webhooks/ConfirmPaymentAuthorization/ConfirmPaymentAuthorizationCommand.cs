using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.ConfirmPaymentAuthorization;

/// <summary>
/// Records the hold a <c>payment_intent.amount_capturable_updated</c> announced — Feature 3.8
/// Milestone F, the reconciling half of §8.
/// <para>
/// Like <c>AttachWebhookPaymentMethodCommand</c> it takes the <b>log row's</b> identifier and
/// nothing else: the intent, the order and the status are read from the row this platform wrote one
/// transaction earlier, which is the one thing on this path a caller did not choose. Taking the
/// <c>pi_…</c> as a parameter would make "treat this intent as authorized" a message anybody who can
/// send a command could compose.
/// </para>
/// <para>
/// Driven only by <c>ProcessOutboxJob</c>, so it carries no validator.
/// </para>
/// </summary>
public sealed record ConfirmPaymentAuthorizationCommand(Guid EventLogId) : ICommand;
