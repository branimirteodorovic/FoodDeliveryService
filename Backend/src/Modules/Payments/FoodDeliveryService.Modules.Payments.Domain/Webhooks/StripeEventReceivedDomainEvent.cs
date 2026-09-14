using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Webhooks;

/// <summary>
/// A verified provider event has been recorded and is waiting to be acted on — §7.6.
/// <para>
/// It carries the identifiers and nothing else. The facts the work needs live on
/// <see cref="StripeEventLog"/>, which the handler reads back: a provider event is acted on by the
/// outbox <em>after</em> the HTTP request has returned, and an event whose payload was copied onto
/// this envelope would be a second, diverging record of what the provider said.
/// </para>
/// </summary>
public sealed class StripeEventReceivedDomainEvent(
    Guid eventLogId,
    string providerEventId,
    string eventType,
    DateTime receivedOnUtc) : DomainEvent
{
    public Guid EventLogId { get; init; } = eventLogId;

    /// <summary>Stripe's own <c>evt_…</c>. On the event so a log line names it without a read.</summary>
    public string ProviderEventId { get; init; } = providerEventId;

    /// <summary>
    /// <c>setup_intent.succeeded</c> and its siblings. The dispatch key — the handler switches on it
    /// rather than on the aggregate, so the routing table is visible in one place.
    /// </summary>
    public string EventType { get; init; } = eventType;

    public DateTime ReceivedOnUtc { get; init; } = receivedOnUtc;
}
