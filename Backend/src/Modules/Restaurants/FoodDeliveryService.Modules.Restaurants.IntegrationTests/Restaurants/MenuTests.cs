using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenu;
using FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;
using Microsoft.Extensions.DependencyInjection;

// The Orders replica types (MenuItem) collide by name with this module's own menu vocabulary —
// alias the namespace so every cross-service assertion reads unambiguously.
using OrdersReplica = FoodDeliveryService.Modules.Orders.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Restaurants;

public class MenuTests : BaseIntegrationTest
{
    public MenuTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateMenuCategory_Should_ReturnConflict_WhenNameIsDuplicate()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        await CreateCategoryAsync(client, restaurantId, "Starters");

        // Act — the duplicate check is case-insensitive.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"restaurants/{restaurantId}/menu-categories",
            new CreateMenuCategory.Request { Name = "STARTERS", DisplayOrder = 2 },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateMenuItem_Should_ReturnNotFound_WhenCategoryBelongsToNoRestaurant()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"restaurants/{restaurantId}/menu-items",
            new CreateMenuItem.Request
            {
                CategoryId = Guid.NewGuid(),
                Name = "Pizza",
                Description = "Cheese pizza",
                Price = 12.5m,
            },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateMenuItem_Should_ReturnBadRequest_WhenPriceIsNotPositive()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        Guid categoryId = await CreateCategoryAsync(client, restaurantId, "Mains");

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"restaurants/{restaurantId}/menu-items",
            new CreateMenuItem.Request
            {
                CategoryId = categoryId,
                Name = "Free Pizza",
                Description = "Cheese pizza",
                Price = 0m,
            },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMenu_Should_ReturnNotFound_WhenRestaurantDoesNotExist()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"restaurants/{Guid.NewGuid()}/menu",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMenu_Should_ReturnEmptyCategories_WhenRestaurantHasNoMenuYet()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        // Act
        MenuResponse menu = await GetMenuAsync(client, restaurantId);

        // Assert — an empty menu is a valid menu, not a 404.
        menu.RestaurantId.Should().Be(restaurantId);
        menu.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMenu_Should_ReturnCategoriesInDisplayOrder_WithTheirItems()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        // Created out of order to prove the query sorts by display_order rather than insertion.
        Guid dessertsId = await CreateCategoryAsync(client, restaurantId, "Desserts", displayOrder: 2);
        Guid startersId = await CreateCategoryAsync(client, restaurantId, "Starters", displayOrder: 1);

        Guid soupId = await CreateItemAsync(client, restaurantId, startersId, "Soup", 4.5m);
        Guid cakeId = await CreateItemAsync(client, restaurantId, dessertsId, "Cake", 6m);

        // Act
        MenuResponse menu = await GetMenuAsync(client, restaurantId);

        // Assert
        menu.Categories.Select(category => category.Id).Should().Equal(startersId, dessertsId);

        MenuCategoryResponse starters = menu.Categories.First();
        starters.Name.Should().Be("Starters");
        starters.Items.Should().ContainSingle();

        MenuItemResponse soup = starters.Items.Single();
        soup.Id.Should().Be(soupId);
        soup.Name.Should().Be("Soup");
        soup.Price.Should().Be(4.5m);
        soup.IsAvailable.Should().BeTrue();

        // Each item is nested under its own category, never duplicated across them.
        menu.Categories.Last().Items.Single().Id.Should().Be(cakeId);
    }

    [Fact]
    public async Task UpdateMenuCategory_Should_RenameAndReorderCategory()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        Guid categoryId = await CreateCategoryAsync(client, restaurantId, "Starters", displayOrder: 1);

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}/menu-categories/{categoryId}",
            new UpdateMenuCategory.Request { Name = "Appetizers", DisplayOrder = 5 },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        MenuResponse menu = await GetMenuAsync(client, restaurantId);
        MenuCategoryResponse category = menu.Categories.Single();
        category.Name.Should().Be("Appetizers");
        category.DisplayOrder.Should().Be(5);
    }

    [Fact]
    public async Task UpdateMenuCategory_Should_ReturnNotFound_WhenCategoryDoesNotExist()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}/menu-categories/{Guid.NewGuid()}",
            new UpdateMenuCategory.Request { Name = "Appetizers", DisplayOrder = 1 },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateMenuItem_Should_ChangeDetailsAndPrice()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        Guid categoryId = await CreateCategoryAsync(client, restaurantId, "Mains");
        Guid itemId = await CreateItemAsync(client, restaurantId, categoryId, "Pizza", 12.5m);

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}/menu-items/{itemId}",
            new UpdateMenuItem.Request
            {
                Name = "Margherita",
                Description = "Tomato, mozzarella, basil",
                Price = 14m,
                PhotoUrl = "https://example.com/margherita.png",
            },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        MenuResponse menu = await GetMenuAsync(client, restaurantId);
        MenuItemResponse item = menu.Categories.Single().Items.Single();
        item.Name.Should().Be("Margherita");
        item.Description.Should().Be("Tomato, mozzarella, basil");
        item.Price.Should().Be(14m);
        item.PhotoUrl.Should().Be("https://example.com/margherita.png");
    }

    [Fact]
    public async Task UpdateMenuItem_Should_ReturnNotFound_WhenItemDoesNotExist()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}/menu-items/{Guid.NewGuid()}",
            new UpdateMenuItem.Request { Name = "Margherita", Description = "Pizza", Price = 14m },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetMenuItemAvailability_Should_MarkItemSoldOut()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        Guid categoryId = await CreateCategoryAsync(client, restaurantId, "Mains");
        Guid itemId = await CreateItemAsync(client, restaurantId, categoryId, "Pizza", 12.5m);

        // Act
        HttpResponseMessage response = await SetAvailabilityAsync(client, restaurantId, itemId, isAvailable: false);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        MenuResponse menu = await GetMenuAsync(client, restaurantId);
        menu.Categories.Single().Items.Single().IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task SetMenuItemAvailability_Should_ReturnNotFound_WhenItemDoesNotExist()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        // Act
        HttpResponseMessage response = await SetAvailabilityAsync(
            client,
            restaurantId,
            Guid.NewGuid(),
            isAvailable: false);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Orders prices every order line from its own MenuItem replica, so the whole menu lifecycle —
    /// add, reprice, sell out — has to reach that replica for placement to charge correctly. Each
    /// hop is async (Restaurants outbox → RabbitMQ → Orders inbox), so every step is polled.
    /// </summary>
    [Fact]
    public async Task MenuItemLifecycle_Should_PropagateToOrdersReplica()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        Guid categoryId = await CreateCategoryAsync(client, restaurantId, "Mains");

        // Act & Assert — MenuItemAddedIntegrationEvent seeds the replica.
        Guid itemId = await CreateItemAsync(client, restaurantId, categoryId, "Pizza", 12.5m);

        OrdersReplica.MenuItem seeded = await WaitForReplicaAsync(
            itemId,
            replica => replica.Price == 12.5m,
            "the added menu item should seed the Orders replica");

        seeded.RestaurantId.Should().Be(restaurantId);
        seeded.Name.Should().Be("Pizza");
        seeded.IsAvailable.Should().BeTrue();

        // A price change collapses onto the same full-snapshot MenuItemUpdatedIntegrationEvent.
        HttpResponseMessage updateResponse = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}/menu-items/{itemId}",
            new UpdateMenuItem.Request { Name = "Margherita", Description = "Pizza", Price = 14m },
            TestContext.Current.CancellationToken);

        updateResponse.EnsureSuccessStatusCode();

        OrdersReplica.MenuItem repriced = await WaitForReplicaAsync(
            itemId,
            replica => replica.Price == 14m,
            "the repriced menu item should reach the Orders replica");

        repriced.Name.Should().Be("Margherita");

        // Selling out must reach Orders too, or placement would accept unavailable items.
        HttpResponseMessage availabilityResponse = await SetAvailabilityAsync(
            client,
            restaurantId,
            itemId,
            isAvailable: false);

        availabilityResponse.EnsureSuccessStatusCode();

        await WaitForReplicaAsync(
            itemId,
            replica => !replica.IsAvailable,
            "the sold-out menu item should reach the Orders replica");
    }

    /// <summary>
    /// Polls the Orders test host's own DI for the menu item replica until it satisfies
    /// <paramref name="predicate"/> — no read endpoint is exposed on the Orders API for its
    /// internal replica. A replica that is absent (or not yet updated) keeps the poller retrying.
    /// </summary>
    private async Task<OrdersReplica.MenuItem> WaitForReplicaAsync(
        Guid menuItemId,
        Func<OrdersReplica.MenuItem, bool> predicate,
        string because)
    {
        Result<OrdersReplica.MenuItem> result = await Poller.WaitAsync<OrdersReplica.MenuItem>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope();

                var repository = scope.ServiceProvider.GetRequiredService<OrdersReplica.IMenuItemReplicaRepository>();

                OrdersReplica.MenuItem? replica = await repository.GetAsync(
                    menuItemId,
                    TestContext.Current.CancellationToken);

                if (replica is null || !predicate(replica))
                {
                    return Result.Failure<OrdersReplica.MenuItem>(Error.NullValue);
                }

                return replica;
            });

        result.IsSuccess.Should().BeTrue(because);

        return result.Value;
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

    private static Task<HttpResponseMessage> SetAvailabilityAsync(
        HttpClient client,
        Guid restaurantId,
        Guid menuItemId,
        bool isAvailable)
    {
        return client.PatchAsJsonAsync(
            $"restaurants/{restaurantId}/menu-items/{menuItemId}/availability",
            new SetMenuItemAvailability.Request { IsAvailable = isAvailable },
            TestContext.Current.CancellationToken);
    }
}
