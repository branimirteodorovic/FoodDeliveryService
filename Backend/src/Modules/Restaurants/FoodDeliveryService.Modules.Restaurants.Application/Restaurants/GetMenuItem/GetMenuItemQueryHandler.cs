using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenuItem;

internal sealed class GetMenuItemQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<GetMenuItemQuery, MenuItemSnapshotResponse>
{
    public async Task<Result<MenuItemSnapshotResponse>> Handle(
        GetMenuItemQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT
                 id AS {nameof(MenuItemSnapshotResponse.Id)},
                 restaurant_id AS {nameof(MenuItemSnapshotResponse.RestaurantId)},
                 name AS {nameof(MenuItemSnapshotResponse.Name)},
                 price AS {nameof(MenuItemSnapshotResponse.Price)},
                 is_available AS {nameof(MenuItemSnapshotResponse.IsAvailable)}
             FROM menu_items
             WHERE id = @MenuItemId
             """;

        MenuItemSnapshotResponse? menuItem =
            await connection.QuerySingleOrDefaultAsync<MenuItemSnapshotResponse>(sql, request);

        if (menuItem is null)
        {
            return Result.Failure<MenuItemSnapshotResponse>(MenuItemErrors.NotFound(request.MenuItemId));
        }

        return menuItem;
    }
}
