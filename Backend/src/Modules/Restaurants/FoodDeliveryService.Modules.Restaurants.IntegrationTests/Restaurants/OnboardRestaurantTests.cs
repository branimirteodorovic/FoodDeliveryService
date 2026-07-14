using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Restaurants;

public class OnboardRestaurantTests : BaseIntegrationTest
{
    public OnboardRestaurantTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task OnboardRestaurant_Should_PropagateToOrdersModule()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        var request = new OnboardRestaurant.Request
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

        // Act
        HttpResponseMessage onboardRestaurantResponse = await client.PostAsJsonAsync(
            "restaurants",
            request,
            TestContext.Current.CancellationToken);

        onboardRestaurantResponse.EnsureSuccessStatusCode();

        Guid restaurantId = await onboardRestaurantResponse.Content.ReadFromJsonAsync<Guid>(
            TestContext.Current.CancellationToken);

        // The replica arrives asynchronously: Restaurants outbox job (≤5s) publishes
        // RestaurantRegisteredIntegrationEvent → RabbitMQ → Orders inbox job (≤5s) dispatches
        // UpsertRestaurantCommand. Poll the Orders test host's own DI for the replica row — no
        // read endpoint is exposed on the Orders API for its internal replica.
        Result<Restaurant> replicaResult = await Poller.WaitAsync<Restaurant>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope();

                var restaurantReplicaRepository =
                    scope.ServiceProvider.GetRequiredService<IRestaurantReplicaRepository>();

                // Restaurant? → Result<Restaurant>: null converts to Failure(Error.NullValue),
                // which keeps the poller retrying until the replica materializes.
                return await restaurantReplicaRepository.GetAsync(
                    restaurantId,
                    TestContext.Current.CancellationToken);
            });

        // Assert
        replicaResult.IsSuccess.Should().BeTrue("The restaurant replica should be consumed by the Orders module");

        Restaurant replica = replicaResult.Value;
        replica.Id.Should().Be(restaurantId);
        replica.Name.Should().Be(request.Name);
        replica.CommissionRate.Should().Be(request.CommissionRate);
    }
}
