using System.Data.Common;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenu;
using FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Caching;

/// <summary>
/// Proves Milestone B of the caching plan (declarative query caching) end-to-end against the real
/// Redis + Postgres testcontainers: <see cref="GetMenuQuery"/> is served from cache on a second
/// read, and — the unambiguous "it's actually cached" assertion — a row mutated directly in
/// Postgres (bypassing the API entirely) still shows the stale, cached value on that next read.
/// </summary>
public class MenuCachingTests : BaseIntegrationTest
{
    public MenuCachingTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetMenu_Should_ServeSecondCallFromCache_WhileTheUnderlyingRowHasChanged()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        Guid categoryId = await CreateCategoryAsync(client, restaurantId, "Mains");
        Guid itemId = await CreateItemAsync(client, restaurantId, categoryId, "Pizza", 12.5m);

        // Act — first read warms the cache with the original price.
        MenuResponse warmed = await GetMenuAsync(client, restaurantId);
        warmed.Categories.Single().Items.Single().Price.Should().Be(12.5m);

        // Mutate the row directly in Postgres, bypassing the command handler (and therefore any
        // cache invalidation) entirely — the only way the second read could see the new price is
        // if the query behavior were NOT caching it.
        await UpdateMenuItemPriceDirectlyAsync(itemId, 99m);

        MenuResponse stillCached = await GetMenuAsync(client, restaurantId);

        // Assert — served from cache, so it still reflects the pre-mutation price.
        stillCached.Categories.Single().Items.Single().Price.Should().Be(12.5m);
    }

    private async Task UpdateMenuItemPriceDirectlyAsync(Guid menuItemId, decimal price)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        await connection.ExecuteAsync(
            "UPDATE menu_items SET price = @Price WHERE id = @MenuItemId",
            new { MenuItemId = menuItemId, Price = price });
    }

    private static async Task<Guid> CreateCategoryAsync(
        HttpClient client,
        Guid restaurantId,
        string name,
        int displayOrder = 1)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"restaurants/{restaurantId}/menu-categories",
            new CreateMenuCategory.Request { Name = name, DisplayOrder = displayOrder },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> CreateItemAsync(
        HttpClient client,
        Guid restaurantId,
        Guid categoryId,
        string name,
        decimal price)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"restaurants/{restaurantId}/menu-items",
            new CreateMenuItem.Request
            {
                CategoryId = categoryId,
                Name = name,
                Description = $"{name} description",
                Price = price,
            },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
    }

    private static async Task<MenuResponse> GetMenuAsync(HttpClient client, Guid restaurantId)
    {
        HttpResponseMessage response = await client.GetAsync(
            $"restaurants/{restaurantId}/menu",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        MenuResponse? menu = await response.Content.ReadFromJsonAsync<MenuResponse>(
            TestContext.Current.CancellationToken);

        menu.Should().NotBeNull();

        return menu!;
    }
}
