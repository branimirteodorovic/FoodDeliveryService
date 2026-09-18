using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.ConfirmPaymentCapture;

/// <summary>
/// Records the capture a <c>payment_intent.succeeded</c> announced — Feature 3.8 Milestone G, the
/// reconciling half of §9.
/// <para>
/// Like every other webhook command it takes the <b>log row's</b> identifier and nothing else: the
/// intent, the order and the status are read from the row this platform wrote one transaction
/// earlier. Taking the <c>pi_…</c> as a parameter would make "treat this payment as captured" — a
/// message that says real money moved — something any caller could compose.
/// </para>
/// <para>
/// Driven only by <c>ProcessOutboxJob</c>, so it carries no validator.
/// </para>
/// </summary>
public sealed record ConfirmPaymentCaptureCommand(Guid EventLogId) : ICommand;
