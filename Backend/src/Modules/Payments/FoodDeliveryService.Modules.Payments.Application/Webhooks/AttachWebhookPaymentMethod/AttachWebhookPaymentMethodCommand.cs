using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.AttachWebhookPaymentMethod;

/// <summary>
/// Records the card a <c>setup_intent.succeeded</c> announced — §1.3 step 1.
/// <para>
/// It takes the <b>log row's</b> identifier and nothing else. The customer and the card are read
/// from that row, which is the one thing on this path a caller did not choose: taking them as
/// parameters would make "attach this card to that customer" a message anybody who can send a
/// command could compose.
/// </para>
/// <para>
/// Driven only by <c>ProcessOutboxJob</c>, so it carries no validator — the value it does take is an
/// identifier this platform minted one transaction earlier.
/// </para>
/// </summary>
public sealed record AttachWebhookPaymentMethodCommand(Guid EventLogId) : ICommand;
