using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;
using FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Restaurants;

public class GetRestaurantTests : BaseIntegrationTest
{
    public GetRestaurantTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Should_ReturnError_WhenUserDoesNotExist()
    {
        // Arrange
        var restaurantId = Guid.NewGuid();

        HttpClient client = await GetAuthenticatedHttpClientAsync();

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"restaurants/{restaurantId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_ReturnRestaurant_WhenRestaurantExists()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        var onboardRequest = new OnboardRestaurant.Request
        {
            Name = Faker.Company.CompanyName(),
            TaxIdentification = Faker.Random.Replace("##########"),
            CuisineType = "Italian",
            Email = Faker.Internet.Email(),
            PhoneNumber = Faker.Phone.PhoneNumber(),
            Street = Faker.Address.StreetAddress(),
            City = Faker.Address.City(),
            PostalCode = Faker.Address.ZipCode(),
            Country = Faker.Address.Country(),
            Latitude = Faker.Address.Latitude(),
            Longitude = Faker.Address.Longitude(),
            CommissionRate = 0.30m,
            ManagerEmail = Faker.Internet.Email(),
            ManagerFirstName = Faker.Name.FirstName(),
            ManagerLastName = Faker.Name.LastName(),
        };

        HttpResponseMessage onboardResponse = await client.PostAsJsonAsync(
            "restaurants",
            onboardRequest,
            TestContext.Current.CancellationToken);

        onboardResponse.EnsureSuccessStatusCode();

        Guid restaurantId = await onboardResponse.Content.ReadFromJsonAsync<Guid>(
            TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"restaurants/{restaurantId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        RestaurantResponse? restaurant = await response.Content.ReadFromJsonAsync<RestaurantResponse>(
            TestContext.Current.CancellationToken);

        restaurant.Should().NotBeNull();
        restaurant!.Id.Should().Be(restaurantId);
    }
}
