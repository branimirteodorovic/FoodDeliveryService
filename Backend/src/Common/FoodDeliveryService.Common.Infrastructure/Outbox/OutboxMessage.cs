namespace FoodDeliveryService.Common.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    public string Type { get; init; }

    public string Content { get; init; }

    public DateTime OccurredOnUtc { get; init; }

    public DateTime? ProcessedOnUtc { get; init; }

    public string? Error { get; init; }

    /// <summary>
    /// The <c>X-Correlation-Id</c> of the request that caused this event, stamped by
    /// <see cref="InsertOutboxMessagesInterceptor"/> and restored around the dispatch by
    /// <c>MessageDispatchScope</c>. A column rather than a field inside <see cref="Content"/>: the
    /// content is the event contract, and a column also answers "which outbox rows belong to this
    /// correlation id?". Nullable, so rows written before this existed still dispatch.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// The W3C <c>traceparent</c> of the span that raised the event, so the dispatch — a separate
    /// trace, seconds later — can link back to the trace that caused it.
    /// </summary>
    public string? TraceParent { get; init; }
}
