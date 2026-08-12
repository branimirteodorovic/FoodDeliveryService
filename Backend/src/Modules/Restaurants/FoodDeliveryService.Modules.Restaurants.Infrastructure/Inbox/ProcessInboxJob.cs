using System.Data;
using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Infrastructure.Correlation;
using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Serialization;
using FoodDeliveryService.Common.Presentation.Correlation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Quartz;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Inbox;

[DisallowConcurrentExecution]
internal sealed class ProcessInboxJob(
    IDbConnectionFactory dbConnectionFactory,
    IServiceScopeFactory serviceScopeFactory,
    IDateTimeProvider dateTimeProvider,
    IOptions<InboxOptions> inboxOptions,
    CorrelationContext correlationContext,
    ILogger<ProcessInboxJob> logger) : IJob
{
    private const string ModuleName = "Restaurants";

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("{Module} - Beginning to process inbox messages", ModuleName);

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();
        await using DbTransaction transaction = await connection.BeginTransactionAsync();

        IReadOnlyList<InboxMessageResponse> inboxMessages = await GetInboxMessagesAsync(connection, transaction);

        foreach (InboxMessageResponse inboxMessage in inboxMessages)
        {
            Exception? exception = null;

            // The producing service's correlation id, carried over the bus on a header and written
            // onto this row by IntegrationEventConsumer. Restoring it here is what ties a consuming
            // handler's log lines back to the request in the OTHER service that caused them.
            using IDisposable dispatch = MessageDispatchScope.Begin(
                correlationContext,
                MessagingDiagnostics.InboxDispatch,
                inboxMessage.Type,
                inboxMessage.CorrelationId,
                inboxMessage.TraceParent);

            try
            {
                IIntegrationEvent integrationEvent = JsonConvert.DeserializeObject<IIntegrationEvent>(
                    inboxMessage.Content,
                    SerializerSettings.Instance)!;

                // OrderId and its siblings, read off the event — the asynchronous half of "search
                // for all logs related to one order".
                using IDisposable businessIds = MessageDispatchScope.PushBusinessIds(integrationEvent);

                // Per MESSAGE, unlike the two per-batch lines around this loop — and on this side it
                // is the only line that ties this service's reaction back to the request in ANOTHER
                // service that caused it.
                logger.LogInformation(
                    "{Module} - Dispatching inbox message {MessageId} of type {MessageType}",
                    ModuleName,
                    inboxMessage.Id,
                    inboxMessage.Type);

                using IServiceScope scope = serviceScopeFactory.CreateScope();

                IEnumerable<IIntegrationEventHandler> handlers = IntegrationEventHandlersFactory.GetHandlers(
                    integrationEvent.GetType(),
                    scope.ServiceProvider,
                    Presentation.AssemblyReference.Assembly);

                foreach (IIntegrationEventHandler integrationEventHandler in handlers)
                {
                    await integrationEventHandler.Handle(integrationEvent, context.CancellationToken);
                }
            }
            catch (Exception caughtException)
            {
                logger.LogError(
                    caughtException,
                    "{Module} - Exception while processing inbox message {MessageId}",
                    ModuleName,
                    inboxMessage.Id);

                exception = caughtException;
            }

            await UpdateInboxMessageAsync(connection, transaction, inboxMessage, exception);
        }

        await transaction.CommitAsync();

        logger.LogInformation("{Module} - Completed processing inbox messages", ModuleName);
    }

    private async Task<IReadOnlyList<InboxMessageResponse>> GetInboxMessagesAsync(
        IDbConnection connection,
        IDbTransaction transaction)
    {
        string sql =
            $"""
             SELECT
                id AS {nameof(InboxMessageResponse.Id)},
                type AS {nameof(InboxMessageResponse.Type)},
                content AS {nameof(InboxMessageResponse.Content)},
                correlation_id AS {nameof(InboxMessageResponse.CorrelationId)},
                trace_parent AS {nameof(InboxMessageResponse.TraceParent)}
             FROM inbox_messages
             WHERE processed_on_utc IS NULL
             ORDER BY occurred_on_utc
             LIMIT @BatchSize
             -- SKIP LOCKED: a scheduler that ticks while another is mid-batch takes the *next*
             -- rows instead of blocking on these. No effect at one replica, where Quartz's
             -- [DisallowConcurrentExecution] already serializes the job — it is the prerequisite
             -- KUBERNETES_PHASE2_PLAN.md §5.1 names for ever running more than one.
             FOR UPDATE SKIP LOCKED
             """;

        IEnumerable<InboxMessageResponse> inboxMessages = await connection.QueryAsync<InboxMessageResponse>(
            sql,
            new { inboxOptions.Value.BatchSize },
            transaction: transaction);

        return inboxMessages.AsList();
    }

    private async Task UpdateInboxMessageAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        InboxMessageResponse inboxMessage,
        Exception? exception)
    {
        const string sql =
            """
            UPDATE inbox_messages
            SET processed_on_utc = @ProcessedOnUtc,
                error = @Error
            WHERE id = @Id
            """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                inboxMessage.Id,
                ProcessedOnUtc = dateTimeProvider.UtcNow,
                Error = exception?.ToString()
            },
            transaction: transaction);
    }

    /// <summary>
    /// The correlation columns are nullable and stay nullable here: a row written before they
    /// existed dispatches exactly as it always did, it simply carries no id to restore.
    /// </summary>
    internal sealed record InboxMessageResponse(
        Guid Id,
        string Type,
        string Content,
        string? CorrelationId,
        string? TraceParent);
}
