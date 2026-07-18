using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
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
/// Milestone E end to end against real Postgres/Redis/RabbitMQ: OrderReadyForPickup creates the
/// Delivery and offers it to the NEAREST available driver; accept assigns and reserves, reject and
/// timeout both fall through to the next-nearest (never re-offering a tried driver), exhausted
/// candidates park the delivery Unassigned, and a cancelled order releases the driver. Each test
/// stages its drivers in a distinct city so the 5 km search radius isolates it from the pool
/// residue of the others (the shared factory shrinks the offer window to 10s, expiry tick 1s).
/// </summary>
public class AssignmentTests : BaseIntegrationTest
{
    public AssignmentTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task OrderReadyForPickup_Should_OfferToNearestDriver_AndAcceptShouldAssign()
    {
        // Arrange — Paris. The near driver is ~100 m from the restaurant, the far one ~2 km.
        const double restaurantLatitude = 48.8566;
        const double restaurantLongitude = 2.3522;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid nearDriverId, HttpClient nearDriverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);
        (Guid farDriverId, _) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.02, restaurantLongitude);

        // Act — the restaurant marks the order ready.
        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);

        // Assert — the NEARER driver gets the offer.
        Result<DeliveryAggregate> offered = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Offered);

        offered.IsSuccess.Should().BeTrue("marking the order ready must create and offer the delivery");
        offered.Value.OfferedDriverId.Should().Be(nearDriverId, "the nearest available driver is offered first");
        offered.Value.OfferExpiresOnUtc.Should().NotBeNull();

        // Act — the offered driver accepts.
        HttpResponseMessage acceptResponse = await nearDriverClient.PostAsync(
            new Uri($"delivery/deliveries/{offered.Value.Id}/accept", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert — assigned to the near driver, who is now Busy and out of the candidate pool.
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<DeliveryAggregate> assigned = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Assigned);

        assigned.IsSuccess.Should().BeTrue();
        assigned.Value.DriverId.Should().Be(nearDriverId);
        assigned.Value.AssignedOnUtc.Should().NotBeNull();

        (await GetDriverStatusAsync(nearDriverClient)).Should().Be(DriverStatus.Busy);

        IReadOnlyCollection<Guid> candidates =
            await FindCandidatesAsync(restaurantLatitude, restaurantLongitude);
        candidates.Should().NotContain(nearDriverId, "a reserved driver must leave the geo pool");
        candidates.Should().Contain(farDriverId);

        // The DeliveryAssigned domain event was processed by the outbox — i.e. the full-snapshot
        // DriverAssignedIntegrationEvent went out to the broker.
        Result<bool> published = await WaitForProcessedOutboxMessageAsync(
            nameof(DeliveryAssignedDomainEvent),
            assigned.Value.Id);
        published.IsSuccess.Should().BeTrue("the DriverAssigned integration event must be published via the outbox");
    }

    [Fact]
    public async Task Reject_Should_OfferNextNearestDriver_AndExhaustionShouldParkUnassigned()
    {
        // Arrange — New York.
        const double restaurantLatitude = 40.7128;
        const double restaurantLongitude = -74.0060;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid nearDriverId, HttpClient nearDriverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);
        (Guid farDriverId, HttpClient farDriverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.02, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);

        Result<DeliveryAggregate> offered = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Offered && d.OfferedDriverId == nearDriverId);
        offered.IsSuccess.Should().BeTrue();

        // Act — the near driver declines.
        HttpResponseMessage rejectResponse = await nearDriverClient.PostAsync(
            new Uri($"delivery/deliveries/{offered.Value.Id}/reject", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert — the farther driver is offered next; the near one is never re-offered.
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<DeliveryAggregate> reOffered = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Offered && d.OfferedDriverId == farDriverId);

        reOffered.IsSuccess.Should().BeTrue("rejection must fall through to the next-nearest candidate");
        reOffered.Value.TriedDriverIds.Should().BeEquivalentTo([nearDriverId, farDriverId]);

        // Act — the last candidate declines too.
        HttpResponseMessage secondReject = await farDriverClient.PostAsync(
            new Uri($"delivery/deliveries/{offered.Value.Id}/reject", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert — candidates exhausted: parked Unassigned (and the near driver was NOT retried).
        secondReject.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<DeliveryAggregate> unassigned = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Unassigned);

        unassigned.IsSuccess.Should().BeTrue("exhausted candidates must park the delivery for support");
        unassigned.Value.TriedDriverIds.Should().BeEquivalentTo([nearDriverId, farDriverId]);
    }

    [Fact]
    public async Task ExpiredOffer_Should_BeReofferedToNextNearestDriver_ByTheQuartzJob()
    {
        // Arrange — London. Nobody touches the offer; only ProcessExpiredOffersJob can move it.
        const double restaurantLatitude = 51.5074;
        const double restaurantLongitude = -0.1278;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid nearDriverId, _) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);
        (Guid farDriverId, _) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.02, restaurantLongitude);

        // Act
        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);

        Result<DeliveryAggregate> offered = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Offered && d.OfferedDriverId == nearDriverId);
        offered.IsSuccess.Should().BeTrue();

        // Assert — the near driver stays silent past the 10s window; the job expires the offer and
        // re-offers to the farther driver, never the first again.
        Result<DeliveryAggregate> reOffered = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Offered && d.OfferedDriverId == farDriverId,
            TimeSpan.FromSeconds(45));

        reOffered.IsSuccess.Should().BeTrue("the expiry job must re-offer a lapsed offer to the next candidate");
        reOffered.Value.TriedDriverIds.Should().BeEquivalentTo([nearDriverId, farDriverId]);
    }

    [Fact]
    public async Task OrderReadyForPickup_Should_ParkUnassigned_WhenNoDriversAreInRadius()
    {
        // Arrange — a restaurant in the middle of the Gulf of Guinea: no driver anywhere near.
        Guid orderId = await PublishOrderReadyForPickupAsync(0.5, 0.5);

        // Assert
        Result<DeliveryAggregate> unassigned = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Unassigned);

        unassigned.IsSuccess.Should().BeTrue("no candidates in radius must park the delivery immediately");
        unassigned.Value.TriedDriverIds.Should().BeEmpty();

        Result<bool> published = await WaitForProcessedOutboxMessageAsync(
            nameof(DeliveryUnassignedDomainEvent),
            unassigned.Value.Id);
        published.IsSuccess.Should().BeTrue("the DeliveryUnassigned integration event must be published");
    }

    [Fact]
    public async Task Accept_Should_FailCleanly_WhenDriverIsAlreadyReservedByAnotherDelivery()
    {
        // Arrange — Tokyo. ONE driver, TWO ready orders: both deliveries get offered to the same
        // driver (an offer alone does not reserve). Accepting the first flips the driver to Busy —
        // the aggregate-level guard that stops two deliveries grabbing the same driver.
        const double restaurantLatitude = 35.6762;
        const double restaurantLongitude = 139.6503;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, HttpClient driverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);

        Guid firstOrderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);
        Guid secondOrderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);

        Result<DeliveryAggregate> firstOffered = await WaitForDeliveryAsync(
            firstOrderId,
            d => d.Status == DeliveryStatus.Offered && d.OfferedDriverId == driverId);
        Result<DeliveryAggregate> secondOffered = await WaitForDeliveryAsync(
            secondOrderId,
            d => d.Status == DeliveryStatus.Offered && d.OfferedDriverId == driverId);

        firstOffered.IsSuccess.Should().BeTrue();
        secondOffered.IsSuccess.Should().BeTrue();

        // Act — accept both; only the first can win.
        HttpResponseMessage firstAccept = await driverClient.PostAsync(
            new Uri($"delivery/deliveries/{firstOffered.Value.Id}/accept", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        HttpResponseMessage secondAccept = await driverClient.PostAsync(
            new Uri($"delivery/deliveries/{secondOffered.Value.Id}/accept", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert — exactly one assignment; the second accept fails cleanly and assigns nothing.
        firstAccept.StatusCode.Should().Be(HttpStatusCode.NoContent);
        secondAccept.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        Result<DeliveryAggregate> firstAssigned = await WaitForDeliveryAsync(
            firstOrderId,
            d => d.Status == DeliveryStatus.Assigned && d.DriverId == driverId);
        firstAssigned.IsSuccess.Should().BeTrue();

        Result<DeliveryAggregate> second = await WaitForDeliveryAsync(secondOrderId, _ => true);
        second.Value.Status.Should().NotBe(DeliveryStatus.Assigned);
        second.Value.DriverId.Should().BeNull();
    }

    [Fact]
    public async Task OrderCancelled_Should_CancelDelivery_AndReleaseTheDriver()
    {
        // Arrange — Sydney. Driver accepts, then the customer cancels the order mid-delivery.
        const double restaurantLatitude = -33.8688;
        const double restaurantLongitude = 151.2093;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, HttpClient driverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);

        Result<DeliveryAggregate> offered = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Offered && d.OfferedDriverId == driverId);
        offered.IsSuccess.Should().BeTrue();

        HttpResponseMessage acceptResponse = await driverClient.PostAsync(
            new Uri($"delivery/deliveries/{offered.Value.Id}/accept", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetDriverStatusAsync(driverClient)).Should().Be(DriverStatus.Busy);

        // Act — Orders publishes the cancellation.
        var eventBus = Factory.Services.GetRequiredService<IEventBus>();
        await eventBus.PublishAsync(
            new OrderCancelledIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                orderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        // Assert — the delivery leg is cancelled and the driver goes back to Available.
        Result<DeliveryAggregate> cancelled = await WaitForDeliveryAsync(
            orderId,
            d => d.Status == DeliveryStatus.Cancelled);
        cancelled.IsSuccess.Should().BeTrue("cancelling the order must cancel the delivery leg");

        Result<DriverStatus> released = await Poller.WaitAsync(TimeSpan.FromSeconds(15), async () =>
        {
            DriverStatus status = await GetDriverStatusAsync(driverClient);

            return status == DriverStatus.Available
                ? Result.Success(status)
                : Result.Failure<DriverStatus>(Error.Failure("Driver.StillBusy", "Driver not released yet"));
        });
        released.IsSuccess.Should().BeTrue("the reserved driver must be released back to Available");

        // The expiry job leaves the cancelled delivery alone thereafter.
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Result<DeliveryAggregate> settled = await WaitForDeliveryAsync(orderId, _ => true);
        settled.Value.Status.Should().Be(DeliveryStatus.Cancelled);
    }

    [Fact]
    public async Task Accept_Should_ReturnForbidden_WhenCallerLacksManageDeliveriesPermission()
    {
        // Arrange — the customer holds deliveries:read only.
        HttpClient customerClient = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await customerClient.PostAsync(
            new Uri($"delivery/deliveries/{Guid.NewGuid()}/accept", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Accept_Should_ReturnUnauthorized_WhenCallerIsAnonymous()
    {
        // Act
        HttpResponseMessage response = await HttpClient.PostAsync(
            new Uri($"delivery/deliveries/{Guid.NewGuid()}/accept", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Onboards, activates, logs in a driver, flips them Available, and stages them in
    /// the geo pool at the given position.</summary>
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

    /// <summary>
    /// Waits until the Delivery outbox has processed (error-free) a message of the given domain
    /// event type whose payload references the delivery — i.e. the corresponding integration event
    /// was published to the broker.
    /// </summary>
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
