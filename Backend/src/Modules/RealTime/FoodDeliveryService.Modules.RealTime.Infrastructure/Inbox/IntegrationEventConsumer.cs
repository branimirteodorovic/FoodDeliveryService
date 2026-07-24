using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Serialization;
using MassTransit;
using Newtonsoft.Json;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Inbox;

/// <summary>
/// Writes the raw event to <c>inbox_messages</c> only — no business logic. Used only for the
/// Milestone D Restaurants events (the durable RestaurantManager replica); every other consumer in
/// this module is a direct <see cref="MassTransit.IConsumer{T}"/> that skips the inbox entirely (see
/// <c>Consumers/OrderStatusConsumer.cs</c>'s XML doc for that departure's justification).
/// </summary>
internal sealed class IntegrationEventConsumer<TIntegrationEvent>(IDbConnectionFactory dbConnectionFactory)
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
            OccurredOnUtc = integrationEvent.OccurredOnUtc
        };

        const string sql =
            """
            INSERT INTO inbox_messages(id, type, content, occurred_on_utc)
            VALUES (@Id, @Type, @Content::json, @OccurredOnUtc)
            """;

        await connection.ExecuteAsync(sql, inboxMessage);
    }
}
