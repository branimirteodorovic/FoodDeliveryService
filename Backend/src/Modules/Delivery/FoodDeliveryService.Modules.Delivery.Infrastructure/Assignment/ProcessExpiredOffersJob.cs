using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.ExpireDeliveryOffer;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Assignment;

/// <summary>
/// Expires lapsed delivery offers and re-offers to the next-nearest candidate. The same
/// job-scans-a-table shape as the outbox/inbox processors: because the offer deadline is a column
/// in Postgres, the job is stateless and inherently durable — a service restart loses no timers,
/// the next tick simply re-finds whatever is still expired. The poll interval bounds how long past
/// the offer window an offer can linger (effective window = [window, window + interval]).
/// </summary>
[DisallowConcurrentExecution]
internal sealed class ProcessExpiredOffersJob(
    IDbConnectionFactory dbConnectionFactory,
    IServiceScopeFactory serviceScopeFactory,
    IDateTimeProvider dateTimeProvider,
    ILogger<ProcessExpiredOffersJob> logger) : IJob
{
    private const string ModuleName = "Delivery";

    public async Task Execute(IJobExecutionContext context)
    {
        IReadOnlyList<Guid> expiredDeliveryIds = await GetExpiredDeliveryIdsAsync();

        if (expiredDeliveryIds.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "{Module} - Found {Count} expired delivery offers",
            ModuleName,
            expiredDeliveryIds.Count);

        foreach (Guid deliveryId in expiredDeliveryIds)
        {
            // One scope (one DbContext/transaction) per delivery, and failures are contained — a
            // delivery the routine cannot advance must not stall the others; the next tick retries
            // it because its state still matches the scan.
            try
            {
                using IServiceScope scope = serviceScopeFactory.CreateScope();

                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                Result result = await sender.Send(
                    new ExpireDeliveryOfferCommand(deliveryId),
                    context.CancellationToken);

                if (result.IsFailure)
                {
                    logger.LogError(
                        "{Module} - Failed to expire the offer for delivery {DeliveryId}: {Error}",
                        ModuleName,
                        deliveryId,
                        result.Error);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "{Module} - Exception while expiring the offer for delivery {DeliveryId}",
                    ModuleName,
                    deliveryId);
            }
        }
    }

    private async Task<IReadOnlyList<Guid>> GetExpiredDeliveryIdsAsync()
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            """
            SELECT id
            FROM deliveries
            WHERE status = @OfferedStatus AND offer_expires_on_utc < @UtcNow
            ORDER BY offer_expires_on_utc
            """;

        IEnumerable<Guid> deliveryIds = await connection.QueryAsync<Guid>(
            sql,
            new
            {
                OfferedStatus = (int)DeliveryStatus.Offered,
                dateTimeProvider.UtcNow
            });

        return deliveryIds.AsList();
    }
}
