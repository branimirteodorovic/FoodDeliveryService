namespace FoodDeliveryService.Common.Infrastructure.Inbox;

public sealed class InboxMessage
{
    public Guid Id { get; init; }

    public string Type { get; init; }

    public string Content { get; init; }

    public DateTime OccurredOnUtc { get; init; }

    public DateTime? ProcessedOnUtc { get; init; }

    public string? Error { get; init; }

    /// <summary>
    /// The <c>X-Correlation-Id</c> of the request that ultimately caused this message, read off the
    /// message header by the consume filter — see <c>CorrelationConsumeFilter{T}</c>. Nullable, so
    /// rows written before this existed still dispatch.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// The W3C <c>traceparent</c> of the consume span that wrote this row. Because MassTransit
    /// propagates trace context over the broker, that span is already inside the producing request's
    /// trace — which is what the dispatch activity links back to.
    /// </summary>
    public string? TraceParent { get; init; }
}
