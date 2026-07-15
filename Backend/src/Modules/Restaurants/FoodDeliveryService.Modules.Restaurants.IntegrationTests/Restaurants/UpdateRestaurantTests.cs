using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;
using FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Restaurants;

public class UpdateRestaurantTests : BaseIntegrationTest
{
    public UpdateRestaurantTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Should_ReturnNotFound_WhenRestaurantDoesNotExist()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"restaurants/{Guid.NewGuid()}",
            CreateRequest(),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_UpdateDetailsAndAddress_WhenRequestIsValid()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        UpdateRestaurant.Request request = CreateRequest();

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}",
            request,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        RestaurantResponse restaurant = await GetRestaurantAsync(client, restaurantId);
        restaurant.Name.Should().Be(request.Name);
        restaurant.TaxIdentification.Should().Be(request.TaxIdentification);
        restaurant.CuisineType.Should().Be(request.CuisineType);
        restaurant.Email.Should().Be(request.Email);
        restaurant.PhoneNumber.Should().Be(request.PhoneNumber);
        restaurant.Street.Should().Be(request.Street);
        restaurant.City.Should().Be(request.City);
        restaurant.PostalCode.Should().Be(request.PostalCode);
        restaurant.Country.Should().Be(request.Country);
    }

    [Fact]
    public async Task Should_ReturnBadRequest_WhenNameIsEmpty()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        UpdateRestaurant.Request request = CreateRequest(name: string.Empty);

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}",
            request,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_BeIdempotent_WhenSubmittedTwiceWithTheSameValues()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        Guid restaurantId = await OnboardRestaurantAsync(client);

        UpdateRestaurant.Request request = CreateRequest();

        HttpResponseMessage firstResponse = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}",
            request,
            TestContext.Current.CancellationToken);

        firstResponse.EnsureSuccessStatusCode();

        // Act — the domain treats an unchanged update as a no-op (no event raised); it must still
        // succeed rather than fail or conflict.
        HttpResponseMessage secondResponse = await client.PutAsJsonAsync(
            $"restaurants/{restaurantId}",
            request,
            TestContext.Current.CancellationToken);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        RestaurantResponse restaurant = await GetRestaurantAsync(client, restaurantId);
        restaurant.Name.Should().Be(request.Name);
    }

    private static UpdateRestaurant.Request CreateRequest(string? name = null)
    {
        return new UpdateRestaurant.Request
        {
            Name = name ?? Faker.Company.CompanyName(),
            TaxIdentification = Faker.Random.Replace("##########"),
            CuisineType = "Mexican",
            Email = Faker.Internet.Email(),
            PhoneNumber = Faker.Phone.PhoneNumber(),
            Street = Faker.Address.StreetAddress(),
            City = Faker.Address.City(),
            PostalCode = Faker.Address.ZipCode(),
            Country = Faker.Address.Country(),
            Latitude = Faker.Address.Latitude(),
            Longitude = Faker.Address.Longitude(),
        };
    }

    private static async Task<RestaurantResponse> GetRestaurantAsync(HttpClient client, Guid restaurantId)
    {
        HttpResponseMessage response = await client.GetAsync(
            $"restaurants/{restaurantId}",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        RestaurantResponse? restaurant = await response.Content.ReadFromJsonAsync<RestaurantResponse>(
            TestContext.Current.CancellationToken);

        restaurant.Should().NotBeNull();

        return restaurant!;
    }
}
