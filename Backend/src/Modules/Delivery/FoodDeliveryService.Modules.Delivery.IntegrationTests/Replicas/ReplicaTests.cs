using AwesomeAssertions;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Domain.Orders;
using FoodDeliveryService.Modules.Delivery.Domain.Restaurants;
using FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Replicas;

/// <summary>
/// Delivery keeps read-only replicas of the restaurant and order data the assignment routine needs
/// (Milestone D). Each event is published over the real broker; Delivery's consumer writes it to the
/// inbox and ProcessInboxJob upserts the replica. Assertions poll until the round-trip lands.
/// </summary>
public class ReplicaTests : BaseIntegrationTest
{
    public ReplicaTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task RestaurantRegistered_Should_CreateRestaurantReplicaWithCoordinates()
    {
        // Arrange
        var restaurantId = Guid.NewGuid();
        double latitude = Faker.Address.Latitude();
        double longitude = Faker.Address.Longitude();

        var eventBus = Factory.Services.GetRequiredService<IEventBus>();

        // Act
        await eventBus.PublishAsync(
            new RestaurantRegisteredIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                restaurantId,
                Guid.NewGuid(),
                "Marios Pizzeria",
                "Italian",
                Faker.Address.StreetAddress(),
                Faker.Address.City(),
                Faker.Address.ZipCode(),
                Faker.Address.Country(),
                latitude,
                longitude,
                0.15m),
            TestContext.Current.CancellationToken);

        // Assert
        Result<Restaurant> replica = await WaitForRestaurantAsync(
            restaurantId,
            r => Close(r.Latitude, latitude) && Close(r.Longitude, longitude));

        replica.IsSuccess.Should().BeTrue("the RestaurantRegistered event must create the replica");
        replica.Value.Name.Should().Be("Marios Pizzeria");
    }

    [Fact]
    public async Task RestaurantAddressUpdated_Should_MoveExistingRestaurantReplica()
    {
        // Arrange — register first, then move it.
        var restaurantId = Guid.NewGuid();
        var eventBus = Factory.Services.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(
            new RestaurantRegisteredIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                restaurantId,
                Guid.NewGuid(),
                "Original Name",
                "Italian",
                Faker.Address.StreetAddress(),
                Faker.Address.City(),
                Faker.Address.ZipCode(),
                Faker.Address.Country(),
                10.0,
                20.0,
                0.15m),
            TestContext.Current.CancellationToken);

        (await WaitForRestaurantAsync(restaurantId, _ => true)).IsSuccess.Should().BeTrue();

        // Act
        await eventBus.PublishAsync(
            new RestaurantAddressUpdatedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                restaurantId,
                "Moved Name",
                Faker.Address.StreetAddress(),
                Faker.Address.City(),
                Faker.Address.ZipCode(),
                Faker.Address.Country(),
                45.42,
                -75.69),
            TestContext.Current.CancellationToken);

        // Assert
        Result<Restaurant> moved = await WaitForRestaurantAsync(
            restaurantId,
            r => Close(r.Latitude, 45.42) && Close(r.Longitude, -75.69));

        moved.IsSuccess.Should().BeTrue("the address-update event must move the replica's coordinates");
        moved.Value.Name.Should().Be("Moved Name");
    }

    [Fact]
    public async Task OrderReadyForPickup_Should_CreateOrderReplicaWithDeliveryCoordinates()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        double deliveryLatitude = Faker.Address.Latitude();
        double deliveryLongitude = Faker.Address.Longitude();
        DateTime placedOnUtc = DateTime.UtcNow.AddMinutes(-20);

        var eventBus = Factory.Services.GetRequiredService<IEventBus>();

        // Act
        await eventBus.PublishAsync(
            new OrderReadyForPickupIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                orderId,
                customerId,
                restaurantId,
                Faker.Address.Latitude(),
                Faker.Address.Longitude(),
                Faker.Address.StreetAddress(),
                Faker.Address.City(),
                Faker.Address.ZipCode(),
                Faker.Address.Country(),
                "Leave at the door",
                deliveryLatitude,
                deliveryLongitude,
                42.50m,
                placedOnUtc),
            TestContext.Current.CancellationToken);

        // Assert
        Result<Order> replica = await WaitForOrderAsync(orderId);

        replica.IsSuccess.Should().BeTrue("the OrderReadyForPickup event must create the order replica");
        replica.Value.CustomerId.Should().Be(customerId);
        replica.Value.RestaurantId.Should().Be(restaurantId);
        replica.Value.DeliveryAddress.Latitude.Should().BeApproximately(deliveryLatitude, 1e-9);
        replica.Value.DeliveryAddress.Longitude.Should().BeApproximately(deliveryLongitude, 1e-9);
        replica.Value.DeliveryAddress.Notes.Should().Be("Leave at the door");
    }

    private static bool Close(double a, double b) => Math.Abs(a - b) < 1e-9;

    private async Task<Result<Restaurant>> WaitForRestaurantAsync(Guid restaurantId, Func<Restaurant, bool> predicate) =>
        await Poller.WaitAsync(TimeSpan.FromSeconds(30), async () =>
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRestaurantsRepository>();

            Restaurant? restaurant =
                await repository.GetAsync(restaurantId, TestContext.Current.CancellationToken);

            return restaurant is not null && predicate(restaurant)
                ? Result.Success(restaurant)
                : Result.Failure<Restaurant>(Error.NotFound("Replica.NotReady", "Restaurant replica not ready"));
        });

    private async Task<Result<Order>> WaitForOrderAsync(Guid orderId) =>
        await Poller.WaitAsync(TimeSpan.FromSeconds(30), async () =>
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOrdersRepository>();

            Order? order = await repository.GetAsync(orderId, TestContext.Current.CancellationToken);

            return order is not null
                ? Result.Success(order)
                : Result.Failure<Order>(Error.NotFound("Replica.NotReady", "Order replica not ready"));
        });
}
