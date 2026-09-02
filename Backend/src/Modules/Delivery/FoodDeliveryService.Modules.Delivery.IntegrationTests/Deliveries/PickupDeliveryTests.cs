using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDeliveries;
using FoodDeliveryService.Modules.Delivery.Application.Drivers.GetDriver;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;
using FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Delivery.Presentation.Drivers;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Deliveries;

/// <summary>
/// Milestone F end to end against real Postgres/Redis/RabbitMQ: the assigned driver marks the
/// delivery picked-up then delivered (publishing OrderPickedUp/OrderDelivered over the bus and
/// returning to the available pool), the ownership guards on those and the read endpoints hold, and
/// the tracking reads surface the driver's name and live position. Each test stages its drivers in a
/// distinct city so the 5 km search radius isolates it from the pool residue of the others.
/// </summary>
public class PickupDeliveryTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task PickedUpThenDelivered_Should_CompleteTheDelivery_PublishBothEvents_AndReleaseTheDriver()
    {
        // Arrange — Berlin. One driver accepts the delivery.
        const double restaurantLatitude = 52.5200;
        const double restaurantLongitude = 13.4050;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, HttpClient driverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);
        DeliveryAggregate delivery = await AcceptOfferedDeliveryAsync(orderId, driverId, driverClient);

        // Act — the assigned driver collects the food.
        HttpResponseMessage pickedUpResponse = await driverClient.PostAsync(
            new Uri($"delivery/deliveries/{delivery.Id}/picked-up", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert — advanced to PickedUp; the driver stays Busy (still on the delivery).
        pickedUpResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<DeliveryAggregate> pickedUp = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.PickedUp);
        pickedUp.IsSuccess.Should().BeTrue();
        pickedUp.Value.PickedUpOnUtc.Should().NotBeNull();
        (await GetDriverStatusAsync(driverClient)).Should().Be(DriverStatus.Busy);

        Result<bool> pickedUpPublished = await WaitForProcessedOutboxMessageAsync(
            nameof(DeliveryPickedUpDomainEvent),
            delivery.Id);
        pickedUpPublished.IsSuccess.Should().BeTrue("the OrderPickedUp integration event must be published via the outbox");

        // Act — the driver completes the delivery.
        HttpResponseMessage deliveredResponse = await driverClient.PostAsync(
            new Uri($"delivery/deliveries/{delivery.Id}/delivered", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert — terminal Delivered; the driver is released to Available and re-enters the pool.
        deliveredResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<DeliveryAggregate> delivered = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Delivered);
        delivered.IsSuccess.Should().BeTrue();
        delivered.Value.DeliveredOnUtc.Should().NotBeNull();

        Result<bool> deliveredPublished = await WaitForProcessedOutboxMessageAsync(
            nameof(DeliveryDeliveredDomainEvent),
            delivery.Id);
        deliveredPublished.IsSuccess.Should().BeTrue("the OrderDelivered integration event must be published via the outbox");

        Result<DriverStatus> released = await Poller.WaitAsync(TimeSpan.FromSeconds(15), async () =>
        {
            DriverStatus status = await GetDriverStatusAsync(driverClient);

            return status == DriverStatus.Available
                ? Result.Success(status)
                : Result.Failure<DriverStatus>(Error.Failure("Driver.StillBusy", "Driver not released yet"));
        });
        released.IsSuccess.Should().BeTrue("delivering must release the driver back to Available");

        IReadOnlyCollection<Guid> candidates =
            await FindCandidatesAsync(restaurantLatitude, restaurantLongitude);
        candidates.Should().Contain(driverId, "a released driver must re-enter the geo pool at their last position");
    }

    [Fact]
    public async Task MarkDelivered_Should_FailCleanly_WhenCallerIsNotTheAssignedDriver()
    {
        // Arrange — Madrid. Driver A is assigned; driver B is a bystander with deliveries:manage.
        const double restaurantLatitude = 40.4168;
        const double restaurantLongitude = -3.7038;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid assignedDriverId, HttpClient assignedClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);
        (_, HttpClient otherDriverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.02, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);
        DeliveryAggregate delivery = await AcceptOfferedDeliveryAsync(orderId, assignedDriverId, assignedClient);
        await assignedClient.PostAsync(
            new Uri($"delivery/deliveries/{delivery.Id}/picked-up", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Act — the other driver tries to complete someone else's delivery.
        HttpResponseMessage response = await otherDriverClient.PostAsync(
            new Uri($"delivery/deliveries/{delivery.Id}/delivered", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert — the domain ownership guard rejects it (NotAssignedDriver → 400).
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MarkPickedUp_Should_ReturnForbidden_WhenCallerLacksManageDeliveriesPermission()
    {
        // Arrange — the customer holds deliveries:read only, not deliveries:manage.
        HttpClient customerClient = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await customerClient.PostAsync(
            new Uri($"delivery/deliveries/{Guid.NewGuid()}/picked-up", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MarkDelivered_Should_ReturnUnauthorized_WhenCallerIsAnonymous()
    {
        // Act
        HttpResponseMessage response = await HttpClient.PostAsync(
            new Uri($"delivery/deliveries/{Guid.NewGuid()}/delivered", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDelivery_Should_ReturnTrackingView_ForAssignedDriverAndAdmin_ButNotForABystander()
    {
        // Arrange — Rome. Driver A is assigned; driver B is an unrelated bystander.
        const double restaurantLatitude = 41.9028;
        const double restaurantLongitude = 12.4964;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid assignedDriverId, HttpClient assignedClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);
        (_, HttpClient otherDriverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.02, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);
        DeliveryAggregate delivery = await AcceptOfferedDeliveryAsync(orderId, assignedDriverId, assignedClient);

        // Act + Assert — the assigned driver sees the full view, incl. the live position.
        HttpResponseMessage assignedResponse = await assignedClient.GetAsync(
            new Uri($"delivery/deliveries/{delivery.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken);
        assignedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        DeliveryResponse? view = await assignedResponse.Content.ReadFromJsonAsync<DeliveryResponse>(
            TestContext.Current.CancellationToken);
        view.Should().NotBeNull();
        view!.Status.Should().Be(DeliveryStatus.Assigned);
        view.DriverId.Should().Be(assignedDriverId);
        view.DriverFirstName.Should().NotBeNullOrWhiteSpace("the assigned driver's name is joined in");
        view.CurrentDriverLatitude.Should().NotBeNull("an assigned driver's live position is surfaced for tracking");

        // Admin bypasses the ownership check.
        HttpResponseMessage adminResponse = await adminClient.GetAsync(
            new Uri($"delivery/deliveries/{delivery.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken);
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // A bystanding driver is neither the customer nor the assigned driver → 404, deliberately
        // the same answer as for a delivery id that does not exist. The ownership predicate is part
        // of the query's WHERE clause, so there is no path on which they could be told apart, and
        // an id they guessed right stays indistinguishable from one they guessed wrong.
        HttpResponseMessage bystanderResponse = await otherDriverClient.GetAsync(
            new Uri($"delivery/deliveries/{delivery.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken);
        bystanderResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDeliveryByOrder_Should_ReturnDelivery_ForTheAssignedDriver()
    {
        // Arrange — Lisbon.
        const double restaurantLatitude = 38.7223;
        const double restaurantLongitude = -9.1393;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, HttpClient driverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);
        await AcceptOfferedDeliveryAsync(orderId, driverId, driverClient);

        // Act
        HttpResponseMessage response = await driverClient.GetAsync(
            new Uri($"delivery/orders/{orderId}/delivery", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        DeliveryResponse? view = await response.Content.ReadFromJsonAsync<DeliveryResponse>(
            TestContext.Current.CancellationToken);
        view.Should().NotBeNull();
        view!.OrderId.Should().Be(orderId);
        view.DriverId.Should().Be(driverId);
    }

    [Fact]
    public async Task GetDeliveries_Should_ListTheDriversOwnDeliveries()
    {
        // Arrange — Vienna.
        const double restaurantLatitude = 48.2082;
        const double restaurantLongitude = 16.3738;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, HttpClient driverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);
        DeliveryAggregate delivery = await AcceptOfferedDeliveryAsync(orderId, driverId, driverClient);

        // Act
        IReadOnlyCollection<DeliverySummaryResponse>? deliveries =
            await driverClient.GetFromJsonAsync<IReadOnlyCollection<DeliverySummaryResponse>>(
                new Uri("delivery/deliveries", UriKind.Relative),
                TestContext.Current.CancellationToken);

        // Assert
        deliveries.Should().NotBeNull();
        deliveries!.Should().Contain(d => d.Id == delivery.Id && d.DriverId == driverId);
    }

    /// <summary>Waits for the delivery to be offered to the driver, accepts it, and returns the
    /// now-Assigned aggregate.</summary>
    private async Task<DeliveryAggregate> AcceptOfferedDeliveryAsync(Guid orderId, Guid driverId, HttpClient driverClient)
    {
        Result<DeliveryAggregate> offered = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Offered && d.OfferedDriverId == driverId);
        offered.IsSuccess.Should().BeTrue("the delivery must be offered to the staged driver");

        HttpResponseMessage acceptResponse = await driverClient.PostAsync(
            new Uri($"delivery/deliveries/{offered.Value.Id}/accept", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<DeliveryAggregate> assigned = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Assigned && d.DriverId == driverId);
        assigned.IsSuccess.Should().BeTrue();

        return assigned.Value;
    }

    private async Task<(Guid DriverId, HttpClient Client)> SetUpAvailableDriverAsync(
        HttpClient adminClient,
        double latitude,
        double longitude)
    {
        (Guid driverId, string email) = await OnboardDriverAsync(adminClient);
        await ActivateDriverAsync(email, Factory.TestUserPassword);
        HttpClient driverClient = await CreateClientForUserAsync(email, Factory.TestUserPassword);

        HttpResponseMessage availabilityResponse = await driverClient.PatchAsJsonAsync(
            "delivery/drivers/me/availability",
            new SetMyAvailability.Request { Available = true },
            TestContext.Current.CancellationToken);
        availabilityResponse.EnsureSuccessStatusCode();

        HttpResponseMessage locationResponse = await driverClient.PostAsJsonAsync(
            "delivery/drivers/me/location",
            new RecordMyLocation.Request { Latitude = latitude, Longitude = longitude },
            TestContext.Current.CancellationToken);
        locationResponse.EnsureSuccessStatusCode();

        return (driverId, driverClient);
    }

    private async Task<Guid> PublishOrderReadyForPickupAsync(double restaurantLatitude, double restaurantLongitude)
    {
        var orderId = Guid.NewGuid();

        var eventBus = Factory.Services.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(
            new OrderReadyForPickupIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                restaurantLatitude,
                restaurantLongitude,
                Faker.Address.StreetAddress(),
                Faker.Address.City(),
                Faker.Address.ZipCode(),
                Faker.Address.Country(),
                "Ring the bell",
                restaurantLatitude + 0.01,
                restaurantLongitude + 0.01,
                42.50m,
                DateTime.UtcNow.AddMinutes(-30)),
            TestContext.Current.CancellationToken);

        return orderId;
    }

    private async Task<Result<DeliveryAggregate>> WaitForDeliveryAsync(
        Guid orderId,
        Func<DeliveryAggregate, bool> predicate,
        TimeSpan? timeout = null) =>
        await Poller.WaitAsync(timeout ?? TimeSpan.FromSeconds(30), async () =>
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDeliveriesRepository>();

            DeliveryAggregate? delivery =
                await repository.GetByOrderIdAsync(orderId, TestContext.Current.CancellationToken);

            return delivery is not null && predicate(delivery)
                ? Result.Success(delivery)
                : Result.Failure<DeliveryAggregate>(
                    Error.NotFound("Delivery.NotReady", "The delivery has not reached the expected state yet"));
        });

    private static async Task<DriverStatus> GetDriverStatusAsync(HttpClient driverClient)
    {
        DriverResponse? profile = await driverClient.GetFromJsonAsync<DriverResponse>(
            new Uri("delivery/drivers/me", UriKind.Relative),
            TestContext.Current.CancellationToken);

        profile.Should().NotBeNull();

        return profile!.Status;
    }

    private async Task<IReadOnlyCollection<Guid>> FindCandidatesAsync(double latitude, double longitude)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        var locationStore = scope.ServiceProvider.GetRequiredService<IDriverLocationStore>();

        IReadOnlyCollection<NearbyDriver> nearby = await locationStore.FindNearestAvailableAsync(
            GeoCoordinate.Create(latitude, longitude).Value,
            radiusKm: 5,
            limit: 10,
            TestContext.Current.CancellationToken);

        return nearby.Select(d => d.DriverId).ToArray();
    }

    private async Task<Result<bool>> WaitForProcessedOutboxMessageAsync(string type, Guid deliveryId)
    {
        return await Poller.WaitAsync(TimeSpan.FromSeconds(30), async () =>
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

            await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

            const string sql =
                """
                SELECT content
                FROM outbox_messages
                WHERE type = @Type AND processed_on_utc IS NOT NULL AND error IS NULL
                """;

            IEnumerable<string> contents = await connection.QueryAsync<string>(sql, new { Type = type });

            return contents.Any(c => c.Contains(deliveryId.ToString(), StringComparison.OrdinalIgnoreCase))
                ? Result.Success(true)
                : Result.Failure<bool>(Error.NotFound(
                    "Outbox.NotProcessed",
                    $"No processed {type} outbox message for delivery {deliveryId} yet"));
        });
    }
}
