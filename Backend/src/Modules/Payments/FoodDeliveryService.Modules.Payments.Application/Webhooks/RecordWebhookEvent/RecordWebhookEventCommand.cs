using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.RecordWebhookEvent;

/// <summary>
/// Verifies and records one provider webhook — Feature 3.8 Milestone E.
/// <para>
/// <b><see cref="Payload"/> is the raw request body and must stay raw.</b> The signature covers the
/// exact bytes Stripe sent, so anything that round-trips them through a model — binding,
/// re-serializing, even normalizing whitespace — breaks the digest and rejects every webhook the
/// platform will ever receive. The endpoint reads the stream and hands the string straight here
/// (§7.2).
/// </para>
/// <para>
/// This command is <b>anonymous</b>: the signature is the credential. There is no caller identity to
/// read, and adding one would mean Stripe holding a token for this platform.
/// </para>
/// </summary>
public sealed record RecordWebhookEventCommand(string Payload, string? Signature) : ICommand;
