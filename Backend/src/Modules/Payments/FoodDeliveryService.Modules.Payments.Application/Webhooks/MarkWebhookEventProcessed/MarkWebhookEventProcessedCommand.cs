using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Webhooks.MarkWebhookEventProcessed;

/// <summary>
/// Closes the log row for a provider event — Feature 3.8 Milestone E.
/// <para>
/// Driven only by <c>ProcessOutboxJob</c> and never by an HTTP caller, which is why it carries no
/// validator: its one field is an identifier this platform minted, and a failure here is a dropped
/// bookkeeping stamp rather than a <c>400</c>.
/// </para>
/// </summary>
/// <param name="Error">
/// Null when the work succeeded. A description when it did not — in which case the row stays
/// <em>unprocessed</em> and carries the reason, because neither outbox job retries and the row is
/// then the only record that this event is still owed some work.
/// </param>
public sealed record MarkWebhookEventProcessedCommand(Guid EventLogId, string? Error) : ICommand;
