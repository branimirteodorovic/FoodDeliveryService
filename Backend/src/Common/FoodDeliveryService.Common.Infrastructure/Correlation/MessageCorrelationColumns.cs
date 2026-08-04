namespace FoodDeliveryService.Common.Infrastructure.Correlation;

/// <summary>
/// The widths of the two correlation columns carried by <c>outbox_messages</c> and
/// <c>inbox_messages</c>. One definition, because eleven tables across six databases have to agree
/// on them and a migration is the expensive place to discover that they don't.
/// </summary>
public static class MessageCorrelationColumns
{
    /// <summary>
    /// Matches the cap <c>CorrelationIdMiddleware</c> enforces on an inbound <c>X-Correlation-Id</c>,
    /// so a value the platform accepted at the edge always fits the column it ends up in.
    /// </summary>
    public const int CorrelationIdMaxLength = 128;

    /// <summary>
    /// A W3C <c>traceparent</c> is exactly 55 characters (<c>00-{32}-{16}-{2}</c>); the headroom is
    /// for a future version prefix, not for arbitrary input — the value is produced by
    /// <see cref="System.Diagnostics.Activity"/>, never by a caller.
    /// </summary>
    public const int TraceParentMaxLength = 64;

    /// <summary>
    /// A correlation id that outgrew its column must never fail the write it is only describing —
    /// not the business transaction the outbox row travels in, and not the inbox insert that makes a
    /// message durable. The edge already bounds inbound ids to the same length, so this can only
    /// fire for a value minted inside the platform, where a truncated id beats a lost message.
    /// </summary>
    public static string? FitCorrelationId(string? value) => Truncate(value, CorrelationIdMaxLength);

    /// <summary>
    /// Same contract as <see cref="FitCorrelationId"/>. A truncated traceparent simply fails to
    /// parse at restore time and degrades to no link, which is the documented behaviour for a
    /// malformed value.
    /// </summary>
    public static string? FitTraceParent(string? value) => Truncate(value, TraceParentMaxLength);

    private static string? Truncate(string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? value[..maxLength] : value;
}
