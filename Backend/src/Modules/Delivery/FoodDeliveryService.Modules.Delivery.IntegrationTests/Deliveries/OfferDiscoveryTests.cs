using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetMyDeliveryOffers;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Delivery.Presentation.Drivers;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Deliveries;

/// <summary>
/// Driver-side discovery of a pending offer, against real Postgres/Redis/RabbitMQ. An offer sets
/// <c>offered_driver_id</c> and leaves <c>driver_id</c> null until the driver accepts, so until
/// this shipped the one person who had to answer the offer could not see it at all.
/// <para>
/// Each test stages its drivers in a city <b>no other suite in this collection uses</b>, so the
/// 5 km search radius isolates it from their pool residue: a driver released back to
/// <c>Available</c> at the end of another test is still in the Redis geo set, and one standing at
/// the same offset from the same restaurant would win the nearest-candidate slot here. The shared
/// factory shrinks the offer window to 10s, expiry tick 1s — short enough that the expiry
/// assertion is quick, long enough that the positive reads run well inside a live offer.
/// </para>
/// </summary>
public class OfferDiscoveryTests : BaseIntegrationTest
{
    public OfferDiscoveryTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task OfferedDriver_Should_SeeTheOffer_OnTheirOwnOffersList()
    {
        // Arrange — Reykjavik.
        const double restaurantLatitude = 64.1466;
        const double restaurantLongitude = -21.9426;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, HttpClient driverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);

        DeliveryAggregate offered = await WaitForOfferAsync(orderId, driverId);

        // Act
        IReadOnlyCollection<DeliveryOfferResponse> offers = await GetMyOffersAsync(driverClient);

        // Assert — the offer screen has everything it needs to decide, in one call.
        DeliveryOfferResponse offer = offers.Should().ContainSingle(o => o.Id == offered.Id).Subject;

        offer.OrderId.Should().Be(orderId);
        offer.RestaurantId.Should().Be(offered.RestaurantId);
        offer.PickupLatitude.Should().BeApproximately(restaurantLatitude, 0.000_001);
        offer.PickupLongitude.Should().BeApproximately(restaurantLongitude, 0.000_001);
        offer.DropoffStreet.Should().NotBeNullOrWhiteSpace();
        offer.DropoffCity.Should().NotBeNullOrWhiteSpace();
        offer.DropoffPostalCode.Should().NotBeNullOrWhiteSpace();
        offer.DropoffCountry.Should().NotBeNullOrWhiteSpace();
        offer.DropoffNotes.Should().Be("Ring the bell");
        offer.DropoffLatitude.Should().BeApproximately(restaurantLatitude + 0.01, 0.000_001);
        offer.DropoffLongitude.Should().BeApproximately(restaurantLongitude + 0.01, 0.000_001);
        offer.OfferExpiresOnUtc.Should().BeCloseTo(offered.OfferExpiresOnUtc!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task OfferedDriver_Should_ReadTheDeliveryById_WhileTheOfferIsLive()
    {
        // Arrange — Nairobi. The driver_id column is still null at this point; this is exactly the
        // read DeliveryAccess.VisibleToCallerSql had to be widened for.
        const double restaurantLatitude = -1.2921;
        const double restaurantLongitude = 36.8219;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, HttpClient driverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);

        DeliveryAggregate offered = await WaitForOfferAsync(orderId, driverId);

        // Act
        HttpResponseMessage byId = await driverClient.GetAsync(
            new Uri($"delivery/deliveries/{offered.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        HttpResponseMessage byOrder = await driverClient.GetAsync(
            new Uri($"delivery/orders/{orderId}/delivery", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — both detail paths share the one predicate, so both must open.
        byId.StatusCode.Should().Be(HttpStatusCode.OK);
        byOrder.StatusCode.Should().Be(HttpStatusCode.OK);

        DeliveryResponse? delivery =
            await byId.Content.ReadFromJsonAsync<DeliveryResponse>(TestContext.Current.CancellationToken);

        delivery.Should().NotBeNull();
        delivery!.Status.Should().Be(DeliveryStatus.Offered);
        delivery.DriverId.Should().BeNull("an offer assigns nobody — that is the whole point of this read");
        delivery.OfferExpiresOnUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task AnotherDriver_Should_NotSeeSomeoneElsesOffer_AndGets404NotForbidden()
    {
        // Arrange — Helsinki. Two drivers: the near one is offered the delivery, the far one is a
        // legitimately authenticated driver with the same permissions and no business here.
        const double restaurantLatitude = 60.1699;
        const double restaurantLongitude = 24.9384;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid nearDriverId, _) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);
        (_, HttpClient farDriverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.02, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);

        DeliveryAggregate offered = await WaitForOfferAsync(orderId, nearDriverId);

        // Act
        IReadOnlyCollection<DeliveryOfferResponse> foreignOffers = await GetMyOffersAsync(farDriverClient);

        HttpResponseMessage byId = await farDriverClient.GetAsync(
            new Uri($"delivery/deliveries/{offered.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        HttpResponseMessage byOrder = await farDriverClient.GetAsync(
            new Uri($"delivery/orders/{orderId}/delivery", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — the security assertion. 404, never 403: a distinguishable "not yours" would
        // confirm the id exists, which is what somebody enumerating delivery ids is after.
        foreignOffers.Should().NotContain(o => o.Id == offered.Id);

        byId.StatusCode.Should().Be(HttpStatusCode.NotFound);
        byOrder.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExpiredOffer_Should_LeaveTheOffersListEmpty()
    {
        // Arrange — Anchorage. Nobody answers; the offer simply lapses, and with a single candidate
        // in radius there is nobody to re-offer to — the delivery parks Unassigned.
        const double restaurantLatitude = 61.2181;
        const double restaurantLongitude = -149.9003;

        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, HttpClient driverClient) =
            await SetUpAvailableDriverAsync(adminClient, restaurantLatitude + 0.001, restaurantLongitude);

        Guid orderId = await PublishOrderReadyForPickupAsync(restaurantLatitude, restaurantLongitude);

        DeliveryAggregate offered = await WaitForOfferAsync(orderId, driverId);
        (await GetMyOffersAsync(driverClient)).Should().Contain(o => o.Id == offered.Id);

        // Act — wait past the (shortened) offer window. The list must go empty on the deadline
        // itself, not when ProcessExpiredOffersJob eventually clears the column: the exclusion is
        // in SQL, so there is no interval in which a driver is shown an offer accept would refuse.
        Result<bool> gone = await Poller.WaitAsync(TimeSpan.FromSeconds(45), async () =>
        {
            IReadOnlyCollection<DeliveryOfferResponse> offers = await GetMyOffersAsync(driverClient);

            return offers.Any(o => o.Id == offered.Id)
                ? Result.Failure<bool>(Error.Failure("Offer.StillListed", "The lapsed offer is still listed"))
                : Result.Success(true);
        });

        // Assert
        gone.IsSuccess.Should().BeTrue("an offer past OfferExpiresOnUtc must be excluded in SQL");
    }

    [Fact]
    public async Task Offers_Should_ReturnForbidden_WhenCallerLacksDriversReadPermission()
    {
        // Arrange — a customer holds deliveries:read but not drivers:read.
        HttpClient customerClient = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await customerClient.GetAsync(
            new Uri("delivery/drivers/me/offers", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Offers_Should_ReturnUnauthorized_WhenCallerIsAnonymous()
    {
        // Act
        HttpResponseMessage response = await HttpClient.GetAsync(
            new Uri("delivery/drivers/me/offers", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<IReadOnlyCollection<DeliveryOfferResponse>> GetMyOffersAsync(HttpClient driverClient)
    {
        HttpResponseMessage response = await driverClient.GetAsync(
            new Uri("delivery/drivers/me/offers", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        IReadOnlyCollection<DeliveryOfferResponse>? offers =
            await response.Content.ReadFromJsonAsync<IReadOnlyCollection<DeliveryOfferResponse>>(
                TestContext.Current.CancellationToken);

        offers.Should().NotBeNull();

        return offers!;
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

    private async Task<DeliveryAggregate> WaitForOfferAsync(Guid orderId, Guid offeredDriverId)
    {
        Result<DeliveryAggregate> offered = await Poller.WaitAsync(TimeSpan.FromSeconds(30), async () =>
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDeliveriesRepository>();

            DeliveryAggregate? delivery =
                await repository.GetByOrderIdAsync(orderId, TestContext.Current.CancellationToken);

            return delivery is { Status: DeliveryStatus.Offered } && delivery.OfferedDriverId == offeredDriverId
                ? Result.Success(delivery)
                : Result.Failure<DeliveryAggregate>(
                    Error.NotFound("Delivery.NotOfferedYet", "The delivery has not been offered to that driver yet"));
        });

        offered.IsSuccess.Should().BeTrue("the delivery must be offered to the staged driver");

        return offered.Value;
    }
}
