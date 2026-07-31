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
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);

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

        await manager.WaitUntilJoinedAsync(
            () => PublishAsync(OrderAccepted(Guid.NewGuid(), Guid.NewGuid(), restaurantId), ct), ct);

        var orderId = Guid.NewGuid();
        await PublishAsync(OrderAccepted(orderId, Guid.NewGuid(), restaurantId), ct);

        // Matched on this order's id, so a probe frame still in flight can't stand in for it.
        RestaurantActivityFrame frame = await manager.ReadNextAsync(f => f.OrderId == orderId, ct);
        frame.Status.Should().Be(OrderStatuses.Accepted);

        // A transition on someone else's restaurant must never reach this manager.
        var foreignOrderId = Guid.NewGuid();
        await PublishAsync(OrderAccepted(foreignOrderId, Guid.NewGuid(), Guid.NewGuid()), ct);
        await manager.AssertNoFrameAsync(f => f.OrderId == foreignOrderId);
    }

    [Fact]
    public async Task SupportAgent_ReceivesActivityForAnyRestaurant()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using TrackedConnection<SupportActivityFrame> support = await ConnectAsync<SupportActivityFrame>(
            await GetSupportAgentAccessTokenAsync(), TrackingHubMethods.SupportActivity, ct);

        await support.WaitUntilJoinedAsync(
            () => PublishAsync(OrderPlaced(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), ct), ct);

        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        await PublishAsync(OrderPlaced(orderId, Guid.NewGuid(), restaurantId), ct);

        SupportActivityFrame frame = await support.ReadNextAsync(f => f.OrderId == orderId, ct);
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
        /// <summary>
        /// Blocks until this connection is provably in its dashboard group.
        /// <para>
        /// <c>StartAsync</c> returns as soon as the client reads the handshake response, which the
        /// server writes <em>before</em> it runs <c>TrackingHub.OnConnectedAsync</c> — and that is
        /// where the group is joined, behind a RestaurantManager replica lookup. So an event
        /// published the instant <c>StartAsync</c> returns can be fanned out to a group this
        /// connection has not joined yet, and the frame is simply never delivered. Group membership
        /// is not observable from the client, so the fan-out itself is the probe: publish throwaway
        /// events until one comes back. Each probe carries its own order id, so the assertions in the
        /// tests match on the id they care about and ignore probe frames still in flight.
        /// </para>
        /// <para>
        /// Nothing here compensates for a production defect — the socket is best-effort by design and
        /// the client re-fetches authoritative state over REST on connect (see <c>TrackingHub</c>).
        /// This is purely the test establishing the happens-before that SignalR does not give it.
        /// </para>
        /// </summary>
        public async Task WaitUntilJoinedAsync(Func<Task> publishProbe, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(JoinTimeout);

            while (true)
            {
                await publishProbe();

                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                attempt.CancelAfter(ProbeInterval);

                try
                {
                    await frames.ReadAsync(attempt.Token);
                    return;
                }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
                {
                    // The join hasn't landed yet — probe again until JoinTimeout gives up for real.
                }
            }
        }

        /// <summary>Reads until a frame the test cares about arrives, discarding the rest.</summary>
        public async Task<TFrame> ReadNextAsync(Func<TFrame, bool> matches, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReceiveTimeout);

            while (true)
            {
                TFrame frame = await frames.ReadAsync(timeout.Token);

                if (matches(frame))
                {
                    return frame;
                }
            }
        }

        /// <summary>
        /// Asserts no frame matching <paramref name="matches"/> arrives within the silence window.
        /// Non-matching frames are ignored rather than failing the assertion, so a probe frame in
        /// flight cannot masquerade as the leak being tested for.
        /// </summary>
        public async Task AssertNoFrameAsync(Func<TFrame, bool> matches)
        {
            using var timeout = new CancellationTokenSource(SilenceWindow);

            Func<Task> read = async () =>
            {
                while (true)
                {
                    TFrame frame = await frames.ReadAsync(timeout.Token);

                    // A matching frame is the leak: return normally so the assertion below fails.
                    if (matches(frame))
                    {
                        return;
                    }
                }
            };

            await read.Should().ThrowAsync<OperationCanceledException>();
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
