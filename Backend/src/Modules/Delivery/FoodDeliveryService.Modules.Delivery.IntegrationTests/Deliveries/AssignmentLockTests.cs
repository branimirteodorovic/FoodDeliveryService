using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Assignment;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;
using FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Delivery.Presentation.Drivers;
using Microsoft.Extensions.DependencyInjection;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Deliveries;

/// <summary>
/// Caching plan Milestone D against real Redis and Postgres: the distributed lock contract itself,
/// and the two assignment races it closes — one delivery offered twice by overlapping triggers, and
/// one driver accepting two of their open offers at the same instant. Each test stages its drivers
/// in its own city so the 5 km search radius isolates it from the other tests' pool residue.
/// </summary>
public class AssignmentLockTests : BaseIntegrationTest
{
    public AssignmentLockTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task DistributedLock_Should_AdmitOneHolder_AndReleaseOnlyItsOwnToken()
    {
        // Arrange — the registered implementation, i.e. Redis SET NX PX + the Lua owner-check.
        var distributedLock = Factory.Services.GetRequiredService<IDistributedLock>();
        string resource = $"delivery:tests:{Guid.NewGuid()}";

        // Act + Assert — a held resource turns the next caller away.
        IAsyncDisposable? holder = await distributedLock.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        holder.Should().NotBeNull();

        IAsyncDisposable? contender = await distributedLock.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        contender.Should().BeNull("Redis decides the winner in a single SET NX");

        // Releasing hands it to the next caller.
        await holder!.DisposeAsync();

        IAsyncDisposable? next = await distributedLock.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        next.Should().NotBeNull("a released lock must be immediately re-acquirable");

        // The first holder disposing again must not delete the lock the new owner is holding —
        // the token comparison inside the release script is what prevents that.
        await holder.DisposeAsync();

        IAsyncDisposable? intruder = await distributedLock.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        intruder.Should().BeNull("a stale handle must never release another caller's lock");

        await next!.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_Should_LapseOnItsTtl_WhenTheHolderNeverReleasesIt()
    {
        // Arrange — the TTL is what keeps a crashed holder from blocking assignment forever.
        var distributedLock = Factory.Services.GetRequiredService<IDistributedLock>();
        string resource = $"delivery:tests:{Guid.NewGuid()}";

        IAsyncDisposable? crashed = await distributedLock.TryAcquireAsync(
            resource,
            TimeSpan.FromMilliseconds(500),
            TestContext.Current.CancellationToken);
        crashed.Should().NotBeNull();

        // Act — nobody releases it; Redis expires the key.
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        // Assert
        await using IAsyncDisposable? next = await distributedLock.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        next.Should().NotBeNull("an abandoned lock must lapse on its TTL");
    }

    [Fact]
    public async Task ConcurrentOfferRoutines_Should_OfferTheDeliveryOnlyOnce()
    {
        // Arrange — Rome. A Pending delivery staged directly (the create path offers immediately),
        // then the offer routine is driven from four scopes at once — the shape of a rejection
        // re-offer landing on top of a ProcessExpiredOffersJob tick, or two replicas ticking it.
        const double restaurantLatitude = 41.9028;
        const double restaurantLongitude = 12.4964;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, _) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);

        Guid deliveryId = await StagePendingDeliveryAsync(restaurantLatitude, restaurantLongitude);

        // Act
        Result[] results = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => OfferNextAsync(deliveryId)));

        // Assert — the delivery is offered to the driver exactly once. Each offer is a state change
        // on the aggregate, so a second one would leave a second DeliveryOfferedDomainEvent in the
        // outbox (and a second push at the driver) even though the row can only show one.
        results.Count(r => r.IsSuccess).Should().BeGreaterThan(0, "one caller must do the work");

        DeliveryAggregate delivery = await GetDeliveryAsync(deliveryId);
        delivery.Status.Should().Be(DeliveryStatus.Offered);
        delivery.OfferedDriverId.Should().Be(driverId);
        delivery.TriedDriverIds.Should().BeEquivalentTo([driverId]);

        int offers = await CountOutboxMessagesAsync(nameof(DeliveryOfferedDomainEvent), deliveryId);
        offers.Should().Be(1, "overlapping triggers must not each offer the same delivery");
    }

    [Fact]
    public async Task ConcurrentAccepts_Should_AssignTheDriverToExactlyOneDelivery()
    {
        // Arrange — Berlin. ONE driver holding TWO open offers (an offer is not a reservation), who
        // accepts both in the same instant. Without the per-driver lock both requests read the
        // driver as Available and both reserve them: one driver, two orders.
        const double restaurantLatitude = 52.5200;
        const double restaurantLongitude = 13.4050;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, HttpClient driverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);

        Guid firstDeliveryId = await StagePendingDeliveryAsync(restaurantLatitude, restaurantLongitude);
        Guid secondDeliveryId = await StagePendingDeliveryAsync(restaurantLatitude, restaurantLongitude);

        (await OfferNextAsync(firstDeliveryId)).IsSuccess.Should().BeTrue();
        (await OfferNextAsync(secondDeliveryId)).IsSuccess.Should().BeTrue();

        (await GetDeliveryAsync(firstDeliveryId)).OfferedDriverId.Should().Be(driverId);
        (await GetDeliveryAsync(secondDeliveryId)).OfferedDriverId.Should().Be(driverId);

        // Act
        HttpResponseMessage[] responses = await Task.WhenAll(
            AcceptAsync(driverClient, firstDeliveryId),
            AcceptAsync(driverClient, secondDeliveryId));

        // Assert — exactly one accept wins; the loser is refused cleanly, not with a 500.
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent)
            .Should().Be(1, "a driver can only be reserved for one delivery");
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(1);

        DeliveryAggregate first = await GetDeliveryAsync(firstDeliveryId);
        DeliveryAggregate second = await GetDeliveryAsync(secondDeliveryId);

        new[] { first, second }.Count(d => d.Status == DeliveryStatus.Assigned)
            .Should().Be(1, "exactly one delivery may end up assigned");
        new[] { first, second }.Count(d => d.DriverId == driverId).Should().Be(1);

        Driver? driver = await GetDriverAsync(driverId);
        driver.Should().NotBeNull();
        driver!.Status.Should().Be(DriverStatus.Busy);

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }
    }

    /// <summary>Onboards, activates and logs in a driver, flips them Available, and stages them in
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

    /// <summary>
    /// Inserts a Pending delivery straight through the repository. The event-driven path
    /// (OrderReadyForPickup → CreateDelivery) offers it in the same breath, which would leave
    /// nothing for these tests to race on.
    /// </summary>
    private async Task<Guid> StagePendingDeliveryAsync(double pickupLatitude, double pickupLongitude)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var repository = scope.ServiceProvider.GetRequiredService<IDeliveriesRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var delivery = DeliveryAggregate.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            GeoCoordinate.Create(pickupLatitude, pickupLongitude).Value,
            new DeliveryAddress(
                Faker.Address.StreetAddress(),
                Faker.Address.City(),
                Faker.Address.ZipCode(),
                Faker.Address.Country(),
                "Ring the bell",
                pickupLatitude + 0.01,
                pickupLongitude + 0.01),
            DateTime.UtcNow);

        repository.Insert(delivery);

        await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return delivery.Id;
    }

    /// <summary>Runs the offer routine in its own scope — one DbContext per caller, exactly as the
    /// job tick, the inbox and an HTTP request each get their own.</summary>
    private async Task<Result> OfferNextAsync(Guid deliveryId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var assignmentService = scope.ServiceProvider.GetRequiredService<IDeliveryAssignmentService>();

        return await assignmentService.OfferNextAsync(deliveryId, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient driverClient, Guid deliveryId) =>
        driverClient.PostAsync(
            new Uri($"delivery/deliveries/{deliveryId}/accept", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

    private async Task<DeliveryAggregate> GetDeliveryAsync(Guid deliveryId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var repository = scope.ServiceProvider.GetRequiredService<IDeliveriesRepository>();

        DeliveryAggregate? delivery = await repository.GetAsync(deliveryId, TestContext.Current.CancellationToken);

        delivery.Should().NotBeNull();

        return delivery!;
    }

    private async Task<Driver?> GetDriverAsync(Guid driverId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var repository = scope.ServiceProvider.GetRequiredService<IDriversRepository>();

        return await repository.GetAsync(driverId, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Counts the outbox rows a domain event of the given type left for this delivery. The
    /// interceptor writes them in the same transaction as the state change, so this is an exact
    /// count of how many times the aggregate actually transitioned — no polling needed.
    /// </summary>
    private async Task<int> CountOutboxMessagesAsync(string type, Guid deliveryId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            """
            SELECT COUNT(*)
            FROM outbox_messages
            WHERE type = @Type AND content::text LIKE @DeliveryIdPattern
            """;

        return await connection.ExecuteScalarAsync<int>(
            sql,
            new { Type = type, DeliveryIdPattern = $"%{deliveryId}%" });
    }
}
