using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.ReleasePayment;

/// <summary>
/// Give up a hold that will never be captured — Feature 3.8 Milestone G, §1.3 step 6. Driven by
/// <c>OrderRejectedIntegrationEvent</c> and <c>OrderCancelledIntegrationEvent</c>, which is why it
/// names the order rather than the reason: both endings mean the same thing to the money.
/// <para>
/// Inbox-driven only, so no validator.
/// </para>
/// <para>
/// <b>It does carry the trigger, and that is not a contradiction of the sentence above.</b> The
/// instruction is the same either way — nothing below branches on it — but Feature 3.8 Milestone I
/// needs <c>payments.released</c> tagged with what ended the order, and this is the last place that
/// fact exists: <c>PaymentReleasedDomainEvent</c> deliberately does not carry it (§9), so a tag taken
/// downstream would have to be invented. See <c>PaymentsDiagnostics.RecordReleased</c>.
/// </para>
/// </summary>
/// <param name="Trigger">One of <see cref="PaymentReleaseTrigger"/>'s two constants.</param>
public sealed record ReleasePaymentCommand(Guid OrderId, string Trigger) : ICommand;
