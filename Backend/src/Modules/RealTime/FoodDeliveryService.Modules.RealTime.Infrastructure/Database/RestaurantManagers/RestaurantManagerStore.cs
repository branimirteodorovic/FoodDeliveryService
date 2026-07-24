using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Modules.RealTime.Application.Abstractions.Data;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Database.RestaurantManagers;

/// <summary>
/// Backs <see cref="IRestaurantManagerStore"/> the same way every other module splits its data
/// access: EF Core (tracked entity + <see cref="IUnitOfWork"/>) for the inbox-driven upserts,
/// Dapper (<see cref="IDbConnectionFactory"/>) for the hot, per-connect read — never
/// <c>DbSet&lt;T&gt;</c> for reads.
/// </summary>
internal sealed class RestaurantManagerStore(
    RealTimeDbContext dbContext,
    IUnitOfWork unitOfWork,
    IDbConnectionFactory dbConnectionFactory) : IRestaurantManagerStore
{
    public async Task UpsertAsync(Guid managerUserId, Guid restaurantId, string restaurantName, CancellationToken cancellationToken = default)
    {
        RestaurantManager? existing = await dbContext.RestaurantManagers
            .SingleOrDefaultAsync(m => m.Id == managerUserId, cancellationToken);

        if (existing is null)
        {
            dbContext.RestaurantManagers.Add(RestaurantManager.Create(managerUserId, restaurantId, restaurantName));
        }
        else
        {
            existing.UpdateRestaurant(restaurantId, restaurantName);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRestaurantNameAsync(Guid restaurantId, string restaurantName, CancellationToken cancellationToken = default)
    {
        List<RestaurantManager> managers = await dbContext.RestaurantManagers
            .Where(m => m.RestaurantId == restaurantId)
            .ToListAsync(cancellationToken);

        if (managers.Count == 0)
        {
            // The restaurant hasn't been registered here yet (or never will be, e.g. race with the
            // registered event) — nothing to rename.
            return;
        }

        foreach (RestaurantManager manager in managers)
        {
            manager.RenameRestaurant(restaurantName);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid?> GetRestaurantIdAsync(Guid managerUserId, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            """
            SELECT restaurant_id
            FROM restaurant_managers
            WHERE id = @ManagerUserId
            """;

        return await connection.QuerySingleOrDefaultAsync<Guid?>(sql, new { ManagerUserId = managerUserId });
    }
}
