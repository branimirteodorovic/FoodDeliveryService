using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;
using FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Restaurants;

public class GetRestaurantsTests : BaseIntegrationTest
{
    public GetRestaurantsTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Should_ReturnOnboardedRestaurant()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        // Act — page size is generous because the collection's database is shared across tests.
        IReadOnlyCollection<RestaurantResponse> restaurants = await GetRestaurantsAsync(client, pageSize: 100);

        // Assert
        restaurants.Should().Contain(restaurant => restaurant.Id == restaurantId);
    }

    [Fact]
    public async Task Should_RespectPageSize()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        await OnboardRestaurantAsync(client);
        await OnboardRestaurantAsync(client);

        // Act
        IReadOnlyCollection<RestaurantResponse> restaurants = await GetRestaurantsAsync(client, pageSize: 1);

        // Assert
        restaurants.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_ReturnDifferentRestaurantsAcrossPages()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        await OnboardRestaurantAsync(client);
        await OnboardRestaurantAsync(client);

        // Act
        IReadOnlyCollection<RestaurantResponse> firstPage = await GetRestaurantsAsync(client, page: 1, pageSize: 1);
        IReadOnlyCollection<RestaurantResponse> secondPage = await GetRestaurantsAsync(client, page: 2, pageSize: 1);

        // Assert — the OFFSET must actually skip, not repeat the first row.
        firstPage.Should().ContainSingle();
        secondPage.Should().ContainSingle();
        secondPage.Single().Id.Should().NotBe(firstPage.Single().Id);
    }

    [Theory]
    [InlineData(0, 20)]    // page is 1-based
    [InlineData(1, 0)]     // page size must be at least 1
    [InlineData(1, 101)]   // page size is capped at 100
    public async Task Should_ReturnBadRequest_WhenPagingIsOutOfBounds(int page, int pageSize)
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"restaurants?page={page}&pageSize={pageSize}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<IReadOnlyCollection<RestaurantResponse>> GetRestaurantsAsync(
        HttpClient client,
        int page = 1,
        int pageSize = 20)
    {
        HttpResponseMessage response = await client.GetAsync(
            $"restaurants?page={page}&pageSize={pageSize}",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        List<RestaurantResponse>? restaurants = await response.Content.ReadFromJsonAsync<List<RestaurantResponse>>(
            TestContext.Current.CancellationToken);

        restaurants.Should().NotBeNull();

        return restaurants!;
    }
}
