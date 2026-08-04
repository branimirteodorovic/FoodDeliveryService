using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Infrastructure.Correlation;
using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Serialization;
using FoodDeliveryService.Common.Presentation.Correlation;
using MassTransit;
using Newtonsoft.Json;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Inbox;

/// <summary>
/// Writes the raw event to <c>inbox_messages</c> only — no business logic. Used only for the
/// Milestone D Restaurants events (the durable RestaurantManager replica); every other consumer in
/// this module is a direct <see cref="MassTransit.IConsumer{T}"/> that skips the inbox entirely (see
/// <c>Consumers/OrderStatusConsumer.cs</c>'s XML doc for that departure's justification).
/// </summary>
internal sealed class IntegrationEventConsumer<TIntegrationEvent>(
    IDbConnectionFactory dbConnectionFactory,
    CorrelationContext correlationContext)
    : IConsumer<TIntegrationEvent>
    where TIntegrationEvent : IntegrationEvent
{
    public async Task Consume(ConsumeContext<TIntegrationEvent> context)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        TIntegrationEvent integrationEvent = context.Message;

        var inboxMessage = new InboxMessage
        {
            Id = integrationEvent.Id,
            Type = integrationEvent.GetType().Name,
            Content = JsonConvert.SerializeObject(integrationEvent, SerializerSettings.Instance),
            OccurredOnUtc = integrationEvent.OccurredOnUtc,

            // The id the producing service put on the message header, read back by
            // CorrelationConsumeFilter. The traceparent needs no header of its own: it falls back to
            // this consume span, which MassTransit has already placed inside the producing trace.
            CorrelationId = MessageCorrelationColumns.FitCorrelationId(correlationContext.CorrelationId),
            TraceParent = MessageCorrelationColumns.FitTraceParent(correlationContext.TraceParent)
        };

        const string sql =
            """
            INSERT INTO inbox_messages(id, type, content, occurred_on_utc, correlation_id, trace_parent)
            VALUES (@Id, @Type, @Content::json, @OccurredOnUtc, @CorrelationId, @TraceParent)
            """;

        await connection.ExecuteAsync(sql, inboxMessage);
    }
}
