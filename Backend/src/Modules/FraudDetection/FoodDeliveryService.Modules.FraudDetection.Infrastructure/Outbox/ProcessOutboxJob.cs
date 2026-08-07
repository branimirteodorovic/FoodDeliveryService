using System.Data;
using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Infrastructure.Correlation;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Common.Infrastructure.Serialization;
using FoodDeliveryService.Common.Presentation.Correlation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Quartz;

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure.Outbox;

[DisallowConcurrentExecution]
internal sealed class ProcessOutboxJob(
    IDbConnectionFactory dbConnectionFactory,
    IServiceScopeFactory serviceScopeFactory,
    IDateTimeProvider dateTimeProvider,
    IOptions<OutboxOptions> outboxOptions,
    CorrelationContext correlationContext,
    ILogger<ProcessOutboxJob> logger) : IJob
{
    private const string ModuleName = "FraudDetection";

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("{Module} - Beginning to process outbox messages", ModuleName);

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();
        await using DbTransaction transaction = await connection.BeginTransactionAsync();

        IReadOnlyList<OutboxMessageResponse> outboxMessages = await GetOutboxMessagesAsync(connection, transaction);

        foreach (OutboxMessageResponse outboxMessage in outboxMessages)
        {
            Exception? exception = null;

            // Restores the correlation of the request that raised this event: the id goes back into
            // the Serilog scope, the ambient context re-seeds the publish that follows, and a
            // dispatch span links back to the trace the event came from. Opened from the row alone,
            // before the content is deserialized, so the failure log below is inside it too.
            using IDisposable dispatch = MessageDispatchScope.Begin(
                correlationContext,
                MessagingDiagnostics.OutboxDispatch,
                outboxMessage.Type,
                outboxMessage.CorrelationId,
                outboxMessage.TraceParent);

            try
            {
                IDomainEvent domainEvent = JsonConvert.DeserializeObject<IDomainEvent>(
                    outboxMessage.Content,
                    SerializerSettings.Instance)!;

                // OrderId and its siblings, read off the event — the asynchronous half of "search
                // for all logs related to one order".
                using IDisposable businessIds = MessageDispatchScope.PushBusinessIds(domainEvent);

                // Per MESSAGE, unlike the two per-batch lines around this loop. It is the line a
                // support agent lands on when they search Seq for a correlation id and ask what
                // happened after the request returned — so it has to be written from inside the
                // restored scope, carrying the id, the dispatch trace and the event's own OrderId.
                logger.LogInformation(
                    "{Module} - Dispatching outbox message {MessageId} of type {MessageType}",
                    ModuleName,
                    outboxMessage.Id,
                    outboxMessage.Type);

                using IServiceScope scope = serviceScopeFactory.CreateScope();

                IEnumerable<IDomainEventHandler> handlers = DomainEventHandlersFactory.GetHandlers(
                    domainEvent.GetType(),
                    scope.ServiceProvider,
                    Application.AssemblyReference.Assembly);

                foreach (IDomainEventHandler domainEventHandler in handlers)
                {
                    await domainEventHandler.Handle(domainEvent, context.CancellationToken);
                }
            }
            catch (Exception caughtException)
            {
                logger.LogError(
                    caughtException,
                    "{Module} - Exception while processing outbox message {MessageId}",
                    ModuleName,
                    outboxMessage.Id);

                exception = caughtException;
            }

            await UpdateOutboxMessageAsync(connection, transaction, outboxMessage, exception);
        }

        await transaction.CommitAsync();

        logger.LogInformation("{Module} - Completed processing outbox messages", ModuleName);
    }

    private async Task<IReadOnlyList<OutboxMessageResponse>> GetOutboxMessagesAsync(
        IDbConnection connection,
        IDbTransaction transaction)
    {
        string sql =
            $"""
             SELECT
                id AS {nameof(OutboxMessageResponse.Id)},
                type AS {nameof(OutboxMessageResponse.Type)},
                content AS {nameof(OutboxMessageResponse.Content)},
                correlation_id AS {nameof(OutboxMessageResponse.CorrelationId)},
                trace_parent AS {nameof(OutboxMessageResponse.TraceParent)}
             FROM outbox_messages
             WHERE processed_on_utc IS NULL
             ORDER BY occurred_on_utc
             LIMIT @BatchSize
             FOR UPDATE
             """;

        IEnumerable<OutboxMessageResponse> outboxMessages = await connection.QueryAsync<OutboxMessageResponse>(
            sql,
            new { outboxOptions.Value.BatchSize },
            transaction: transaction);

        return outboxMessages.ToList();
    }

    private async Task UpdateOutboxMessageAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        OutboxMessageResponse outboxMessage,
        Exception? exception)
    {
        const string sql =
            """
            UPDATE outbox_messages
            SET processed_on_utc = @ProcessedOnUtc,
                error = @Error
            WHERE id = @Id
            """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                outboxMessage.Id,
                ProcessedOnUtc = dateTimeProvider.UtcNow,
                Error = exception?.ToString()
            },
            transaction: transaction);
    }

    /// <summary>
    /// The correlation columns are nullable and stay nullable here: a row written before they
    /// existed dispatches exactly as it always did, it simply carries no id to restore.
    /// </summary>
    internal sealed record OutboxMessageResponse(
        Guid Id,
        string Type,
        string Content,
        string? CorrelationId,
        string? TraceParent);
}
