using System.Threading.Channels;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.RealTime.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using Microsoft.AspNetCore.SignalR.Client;

namespace FoodDeliveryService.Modules.RealTime.IntegrationTests.Tracking;

/// <summary>
/// Milestone D: the restaurant dashboard and support dashboard channels. A RestaurantManager's
/// replica row is built off a published <c>RestaurantRegisteredIntegrationEvent</c> (via the durable
/// inbox — see <c>RealTimeModule.ConfigureConsumers</c>), then an Orders/Delivery lifecycle event
/// fans out <c>RestaurantActivity</c> to that restaurant's group and <c>SupportActivity</c> to the
/// single global support group.
/// </summary>
public class DashboardFanOutTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReplicaTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task RestaurantManager_ReceivesActivityForTheirOwnRestaurantOnly()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var restaurantId = Guid.NewGuid();

        await PublishAsync(RestaurantRegistered(restaurantId, Factory.RestaurantManagerUserId), ct);
        Guid? replicatedRestaurantId = await WaitForRestaurantManagerReplicaAsync(Factory.RestaurantManagerUserId, ReplicaTimeout, ct);
        replicatedRestaurantId.Should().Be(restaurantId);

        await using TrackedConnection<RestaurantActivityFrame> manager = await ConnectAsync<RestaurantActivityFrame>(
            await GetRestaurantManagerAccessTokenAsync(), TrackingHubMethods.RestaurantActivity, ct);

        var orderId = Guid.NewGuid();
        await PublishAsync(OrderAccepted(orderId, Guid.NewGuid(), restaurantId), ct);

        RestaurantActivityFrame frame = await manager.ReadNextAsync(ct);
        frame.OrderId.Should().Be(orderId);
        frame.Status.Should().Be(OrderStatuses.Accepted);

        // A transition on someone else's restaurant must never reach this manager.
        await PublishAsync(OrderAccepted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), ct);
        await manager.AssertNoFrameAsync();
    }

    [Fact]
    public async Task SupportAgent_ReceivesActivityForAnyRestaurant()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using TrackedConnection<SupportActivityFrame> support = await ConnectAsync<SupportActivityFrame>(
            await GetSupportAgentAccessTokenAsync(), TrackingHubMethods.SupportActivity, ct);

        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        await PublishAsync(OrderPlaced(orderId, Guid.NewGuid(), restaurantId), ct);

        SupportActivityFrame frame = await support.ReadNextAsync(ct);
        frame.OrderId.Should().Be(orderId);
        frame.RestaurantId.Should().Be(restaurantId);
        frame.Status.Should().Be(OrderStatuses.Placed);
    }

    private async Task<TrackedConnection<TFrame>> ConnectAsync<TFrame>(
        string accessToken,
        string hubMethod,
        CancellationToken cancellationToken)
    {
        HubConnection connection = BuildHubConnection(accessToken);

        var channel = Channel.CreateUnbounded<TFrame>();
        connection.On<TFrame>(hubMethod, frame => channel.Writer.TryWrite(frame));

        await connection.StartAsync(cancellationToken);

        return new TrackedConnection<TFrame>(connection, channel.Reader);
    }

    private static RestaurantRegisteredIntegrationEvent RestaurantRegistered(Guid restaurantId, Guid managerUserId) =>
        new(
            Guid.NewGuid(), DateTime.UtcNow, restaurantId, managerUserId,
            name: "Test Restaurant", cuisineType: "Test Cuisine",
            street: "1 Main St", city: "Town", postalCode: "0000", country: "Country",
            latitude: 1, longitude: 2, commissionRate: 0.1m);

    private static OrderPlacedIntegrationEvent OrderPlaced(Guid orderId, Guid customerId, Guid restaurantId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, customerId, restaurantId, subtotal: 42m, placedOnUtc: DateTime.UtcNow);

    private static OrderAcceptedIntegrationEvent OrderAccepted(Guid orderId, Guid customerId, Guid restaurantId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, customerId, restaurantId, acceptedOnUtc: DateTime.UtcNow);

    /// <summary>A connected hub client whose frames of one server→client method stream into a channel.</summary>
    private sealed class TrackedConnection<TFrame>(HubConnection connection, ChannelReader<TFrame> frames) : IAsyncDisposable
    {
        public async Task<TFrame> ReadNextAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReceiveTimeout);

            return await frames.ReadAsync(timeout.Token);
        }

        public async Task AssertNoFrameAsync()
        {
            using var timeout = new CancellationTokenSource(SilenceWindow);

            Func<Task> read = async () => await frames.ReadAsync(timeout.Token);

            await read.Should().ThrowAsync<OperationCanceledException>();
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
