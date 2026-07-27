using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenu;
using FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Caching;

/// <summary>
/// Milestone C of the caching plan, and the inverse of <see cref="MenuCachingTests"/>: where that
/// suite proves the menu really is cached (a row changed behind the API's back stays stale), this
/// one proves a change made *through* the API is visible on the very next read — no TTL wait, no
/// outbox lag. Every mutation runs against the real endpoint through the full pipeline, with the
/// real Redis and Postgres testcontainers behind it.
/// </summary>
public class MenuInvalidationTests : BaseIntegrationTest
{
    public MenuInvalidationTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetMenu_Should_ReturnTheNewPrice_AfterTheItemIsRepricedThroughTheApi()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        Guid categoryId = await CreateCategoryAsync(client, restaurantId, "Mains");
        Guid itemId = await CreateItemAsync(client, restaurantId, categoryId, "Pizza", 12.5m);

        MenuResponse warmed = await GetMenuAsync(client, restaurantId);
        warmed.Categories.Single().Items.Single().Price.Should().Be(12.5m);

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}/menu-items/{itemId}",
            new UpdateMenuItem.Request
            {
                Name = "Pizza Margherita",
                Description = "Tomato, mozzarella, basil",
                Price = 15m,
            },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        // Assert
        MenuResponse fresh = await GetMenuAsync(client, restaurantId);
        MenuItemResponse item = fresh.Categories.Single().Items.Single();

        item.Price.Should().Be(15m);
        item.Name.Should().Be("Pizza Margherita");
    }

    [Fact]
    public async Task GetMenu_Should_ReflectSoldOut_AfterAvailabilityIsToggledThroughTheApi()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        Guid categoryId = await CreateCategoryAsync(client, restaurantId, "Mains");
        Guid itemId = await CreateItemAsync(client, restaurantId, categoryId, "Pasta", 11m);

        MenuResponse warmed = await GetMenuAsync(client, restaurantId);
        warmed.Categories.Single().Items.Single().IsAvailable.Should().BeTrue();

        // Act — sell the item out.
        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"restaurants/{restaurantId}/menu-items/{itemId}/availability",
            new SetMenuItemAvailability.Request { IsAvailable = false },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        // Assert
        MenuResponse fresh = await GetMenuAsync(client, restaurantId);
        fresh.Categories.Single().Items.Single().IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetMenu_Should_IncludeTheNewItem_AfterItIsAddedThroughTheApi()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        Guid categoryId = await CreateCategoryAsync(client, restaurantId, "Mains");
        await CreateItemAsync(client, restaurantId, categoryId, "Pizza", 12.5m);

        MenuResponse warmed = await GetMenuAsync(client, restaurantId);
        warmed.Categories.Single().Items.Should().HaveCount(1);

        // Act
        await CreateItemAsync(client, restaurantId, categoryId, "Lasagne", 13m);

        // Assert
        MenuResponse fresh = await GetMenuAsync(client, restaurantId);

        fresh.Categories.Single().Items.Should().HaveCount(2);
        fresh.Categories.Single().Items.Select(item => item.Name).Should().Contain("Lasagne");
    }

    [Fact]
    public async Task GetMenu_Should_ShowTheRenamedCategory_AfterItIsUpdatedThroughTheApi()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        Guid categoryId = await CreateCategoryAsync(client, restaurantId, "Mains");

        MenuResponse warmed = await GetMenuAsync(client, restaurantId);
        warmed.Categories.Single().Name.Should().Be("Mains");

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}/menu-categories/{categoryId}",
            new UpdateMenuCategory.Request { Name = "Main Courses", DisplayOrder = 1 },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        // Assert
        MenuResponse fresh = await GetMenuAsync(client, restaurantId);
        fresh.Categories.Single().Name.Should().Be("Main Courses");
    }

    [Fact]
    public async Task GetMenu_Should_IncludeTheNewCategory_AfterItIsAddedThroughTheApi()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);
        await CreateCategoryAsync(client, restaurantId, "Mains");

        MenuResponse warmed = await GetMenuAsync(client, restaurantId);
        warmed.Categories.Should().HaveCount(1);

        // Act
        await CreateCategoryAsync(client, restaurantId, "Desserts", displayOrder: 2);

        // Assert
        MenuResponse fresh = await GetMenuAsync(client, restaurantId);

        fresh.Categories.Should().HaveCount(2);
        fresh.Categories.Select(category => category.Name).Should().Contain("Desserts");
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
