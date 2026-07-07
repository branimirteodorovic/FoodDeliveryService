using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenu;

internal sealed class GetMenuQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<GetMenuQuery, MenuResponse>
{
    private sealed record CategoryRow(Guid Id, string Name, int DisplayOrder);

    private sealed record ItemRow(
        Guid Id,
        Guid CategoryId,
        string Name,
        string Description,
        decimal Price,
        string? PhotoUrl,
        bool IsAvailable);

    public async Task<Result<MenuResponse>> Handle(GetMenuQuery request, CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT id FROM restaurants WHERE id = @RestaurantId;

             SELECT
                 id AS {nameof(CategoryRow.Id)},
                 name AS {nameof(CategoryRow.Name)},
                 display_order AS {nameof(CategoryRow.DisplayOrder)}
             FROM menu_categories
             WHERE restaurant_id = @RestaurantId
             ORDER BY display_order, name;

             SELECT
                 id AS {nameof(ItemRow.Id)},
                 category_id AS {nameof(ItemRow.CategoryId)},
                 name AS {nameof(ItemRow.Name)},
                 description AS {nameof(ItemRow.Description)},
                 price AS {nameof(ItemRow.Price)},
                 photo_url AS {nameof(ItemRow.PhotoUrl)},
                 is_available AS {nameof(ItemRow.IsAvailable)}
             FROM menu_items
             WHERE restaurant_id = @RestaurantId
             ORDER BY name;
             """;

        await using SqlMapper.GridReader reader = await connection.QueryMultipleAsync(sql, request);

        var restaurantId = await reader.ReadSingleOrDefaultAsync<Guid?>();

        if (restaurantId is null)
        {
            return Result.Failure<MenuResponse>(RestaurantErrors.NotFound(request.RestaurantId));
        }

        var categories = (await reader.ReadAsync<CategoryRow>()).ToList();

        var itemsByCategory = (await reader.ReadAsync<ItemRow>())
            .ToLookup(item => item.CategoryId);

        var categoryResponses = categories
            .Select(category => new MenuCategoryResponse(
                category.Id,
                category.Name,
                category.DisplayOrder,
                itemsByCategory[category.Id]
                    .Select(item => new MenuItemResponse(
                        item.Id,
                        item.Name,
                        item.Description,
                        item.Price,
                        item.PhotoUrl,
                        item.IsAvailable))
                    .ToList()))
            .ToList();

        return new MenuResponse(request.RestaurantId, categoryResponses);
    }
}
