using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Webhooks;

/// <summary>
/// One verified provider event, recorded the moment it arrives — Feature 3.8 Milestone E, §7.4.
/// <para>
/// <b>This is the dedupe table, and it is not <c>inbox_messages</c>.</b> Stripe redelivers on any
/// non-2xx and on its own schedule; the inbox is for MassTransit envelopes arriving over RabbitMQ
/// and knows nothing about an <c>evt_…</c>. The unique index on <see cref="ProviderEventId"/> is
/// what makes a redelivery a no-op: the insert loses, and the endpoint answers <c>200</c> without
/// doing the work twice.
/// </para>
/// <para>
/// <b>It records the provider's facts, not the provider's payload.</b> Storing the raw JSON would
/// keep a copy of whatever Stripe chose to send, indefinitely, in a database this platform backs up
/// — including fields §0.5 spent the whole feature keeping out. The five neutral fields below are
/// what any handler actually reads, and a question the log cannot answer is answerable from the
/// Stripe dashboard against <see cref="ProviderEventId"/>.
/// </para>
/// <para>
/// <b>Arrival order means nothing (§7.5).</b> <c>payment_intent.succeeded</c> can land before the
/// event that logically precedes it, so <see cref="ObjectStatus"/> — the status *on the payload* —
/// is what a handler decides from. Nothing here is ordered by <see cref="ReceivedOnUtc"/>.
/// </para>
/// </summary>
public sealed class StripeEventLog : Entity
{
    private StripeEventLog()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Stripe's <c>evt_…</c>. Unique across the table — see the class remarks.</summary>
    public string ProviderEventId { get; private set; }

    /// <summary><c>setup_intent.succeeded</c>, <c>payment_intent.amount_capturable_updated</c>, …</summary>
    public string EventType { get; private set; }

    /// <summary>The object the event is about: <c>seti_…</c>, <c>pi_…</c>, <c>re_…</c>.</summary>
    public string? ObjectId { get; private set; }

    /// <summary>
    /// That object's status as the payload reports it. The one field a handler is allowed to branch
    /// on, because it is a fact about the object rather than a fact about the delivery.
    /// </summary>
    public string? ObjectStatus { get; private set; }

    /// <summary>The Stripe customer (<c>cus_…</c>) the event concerns, when it names one.</summary>
    public string? CustomerReference { get; private set; }

    /// <summary>The Stripe payment method (<c>pm_…</c>) the event names, when it names one.</summary>
    public string? PaymentMethodReference { get; private set; }

    /// <summary>
    /// This platform's own order id, echoed back by the provider from the intent's metadata —
    /// Milestone F. The only field on this row that did not originate with Stripe, and the one that
    /// lets a <c>payment_intent.*</c> be resolved to a payment whose <c>pi_…</c> was never recorded
    /// because the call that would have recorded it did not come back.
    /// </summary>
    public string? OrderReference { get; private set; }

    /// <summary>
    /// For a failure event, a bounded <c>PaymentFailureReason</c> — already mapped, never Stripe's
    /// own message. Stored for the same reason the four references above are: §7.6 defers the work
    /// past the response, so every fact the work needs has to be on the row rather than in a copy of
    /// the provider's payload (§0.5).
    /// <para>
    /// Not to be confused with <see cref="Error"/>, two properties below: this is why the
    /// <em>payment</em> failed, and that is why <em>this platform</em> failed to act on the event.
    /// A row can carry either, both or neither.
    /// </para>
    /// </summary>
    public string? FailureReason { get; private set; }

    public DateTime ReceivedOnUtc { get; private set; }

    /// <summary>
    /// When the outbox finished acting on it. Bookkeeping rather than the dedupe: the unique index
    /// already refuses a second insert, so a crash between the work and this stamp costs an
    /// operator a question, not a customer a second charge.
    /// </summary>
    public DateTime? ProcessedOnUtc { get; private set; }

    /// <summary>Why acting on it failed, for the operator reading this table after the fact.</summary>
    public string? Error { get; private set; }

    public bool IsProcessed => ProcessedOnUtc is not null;

    /// <summary>
    /// Records a verified event. The signature has already been checked by the time this is called —
    /// nothing in the Domain can check it, and an unverified event must never reach a row at all.
    /// <para>
    /// The domain event raised here is the whole of §7.6: the request's remaining work is one insert
    /// and a <c>200</c>, and the outbox picks the event up a second later. Doing the work inline
    /// instead would put a Stripe round trip inside a 20-second provider timeout.
    /// </para>
    /// </summary>
    public static Result<StripeEventLog> Record(
        Guid eventLogId,
        string providerEventId,
        string eventType,
        string? objectId,
        string? objectStatus,
        string? customerReference,
        string? paymentMethodReference,
        string? orderReference,
        string? failureReason,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            return Result.Failure<StripeEventLog>(StripeEventLogErrors.ProviderEventIdRequired);
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            return Result.Failure<StripeEventLog>(StripeEventLogErrors.EventTypeRequired);
        }

        var log = new StripeEventLog
        {
            Id = eventLogId,
            ProviderEventId = providerEventId,
            EventType = eventType,
            ObjectId = objectId,
            ObjectStatus = objectStatus,
            CustomerReference = customerReference,
            PaymentMethodReference = paymentMethodReference,
            OrderReference = orderReference,
            FailureReason = failureReason,
            ReceivedOnUtc = utcNow
        };

        log.Raise(new StripeEventReceivedDomainEvent(eventLogId, providerEventId, eventType, utcNow));

        return log;
    }

    /// <summary>
    /// Marks the event done. Idempotent, and deliberately so: <c>ProcessOutboxJob</c> dispatches at
    /// least once, and a second dispatch that overwrote the first timestamp would make the log say
    /// the event was handled later than it was.
    /// </summary>
    public void MarkProcessed(DateTime utcNow)
    {
        if (IsProcessed)
        {
            return;
        }

        ProcessedOnUtc = utcNow;
        Error = null;
    }

    /// <summary>
    /// Records that acting on the event failed. It is <b>not</b> marked processed, so the row still
    /// reads as outstanding — which matters because neither outbox job retries (§5.6): this row and
    /// its <see cref="Error"/> are the only trace that the platform owes this event some work.
    /// </summary>
    public void MarkFailed(string error)
    {
        if (IsProcessed)
        {
            return;
        }

        Error = error;
    }
}
