using System.Threading.Channels;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.RealTime.IntegrationTests.Abstractions;
using Microsoft.AspNetCore.SignalR.Client;

namespace FoodDeliveryService.Modules.RealTime.IntegrationTests.Tracking;

/// <summary>
/// Driver-side offer discovery: a <c>DeliveryOfferedIntegrationEvent</c> published on the bus
/// becomes a live <c>DeliveryOffered</c> frame in the offered driver's own group — and nobody
/// else's. Drives the real path end to end: IEventBus → RabbitMQ → <c>DeliveryOfferedConsumer</c>
/// → SignalR group → the connected client.
/// <para>
/// A driver is a user here, so the seeded test user stands in for the offered driver: their
/// module-side id is what <c>Driver.Id</c> would be, and it is the <c>sub</c> the hub joins
/// <c>user:{id}</c> from. That identity is the whole reason this needs no new group type.
/// </para>
/// </summary>
public class DeliveryOfferFanOutTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task OfferedDelivery_IsPushedToTheOfferedDriver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection driver = await ConnectAsync(await GetAccessTokenAsync(), ct);
        await driver.WaitUntilJoinedAsync(() => PublishOfferAsync(Guid.NewGuid(), Factory.TestUserId, ct), ct);

        var deliveryId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        DateTime expiresOnUtc = DateTime.UtcNow.AddSeconds(30);

        await PublishAsync(Offered(deliveryId, orderId, Factory.TestUserId, expiresOnUtc), ct);

        DeliveryOfferFrame frame = await driver.ReadNextAsync(f => f.DeliveryId == deliveryId, ct);
        frame.OrderId.Should().Be(orderId);
        frame.OfferExpiresOnUtc.Should().BeCloseTo(expiresOnUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task OfferForAnotherDriver_IsNotPushedToThisDriver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection driver = await ConnectAsync(await GetAccessTokenAsync(), ct);
        await driver.WaitUntilJoinedAsync(() => PublishOfferAsync(Guid.NewGuid(), Factory.TestUserId, ct), ct);

        // Same event shape, addressed to a different driver. The connected client is in
        // user:{TestUserId} and nothing here is ever broadcast, so it must stay silent.
        var foreignDeliveryId = Guid.NewGuid();
        await PublishOfferAsync(foreignDeliveryId, driverId: Guid.NewGuid(), ct);

        await driver.AssertNoFrameAsync(f => f.DeliveryId == foreignDeliveryId);
    }

    [Fact]
    public async Task Offer_IsNotPushedToAnotherConnectedSubject()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        // A second, differently-authenticated client on the same hub. It holds the support-dashboard
        // permission — the broadest audience this service has — and still must not see an offer
        // addressed to a driver: the offer is a driver-private frame, not activity.
        await using TrackedConnection driver = await ConnectAsync(await GetAccessTokenAsync(), ct);
        await using TrackedConnection bystander = await ConnectAsync(await GetSupportAgentAccessTokenAsync(), ct);

        await driver.WaitUntilJoinedAsync(() => PublishOfferAsync(Guid.NewGuid(), Factory.TestUserId, ct), ct);

        var deliveryId = Guid.NewGuid();
        await PublishOfferAsync(deliveryId, Factory.TestUserId, ct);

        // The offered driver gets it...
        DeliveryOfferFrame frame = await driver.ReadNextAsync(f => f.DeliveryId == deliveryId, ct);
        frame.DeliveryId.Should().Be(deliveryId);

        // ...and the other connected subject never does.
        await bystander.AssertNoFrameAsync(f => f.DeliveryId == deliveryId);
    }

    private Task PublishOfferAsync(Guid deliveryId, Guid driverId, CancellationToken cancellationToken) =>
        PublishAsync(Offered(deliveryId, Guid.NewGuid(), driverId, DateTime.UtcNow.AddSeconds(30)), cancellationToken);

    private static DeliveryOfferedIntegrationEvent Offered(
        Guid deliveryId,
        Guid orderId,
        Guid driverId,
        DateTime offerExpiresOnUtc) =>
        new(Guid.NewGuid(), DateTime.UtcNow, deliveryId, orderId, driverId, offerExpiresOnUtc);

    private async Task<TrackedConnection> ConnectAsync(string accessToken, CancellationToken cancellationToken)
    {
        HubConnection connection = BuildHubConnection(accessToken);

        var channel = Channel.CreateUnbounded<DeliveryOfferFrame>();
        connection.On<DeliveryOfferFrame>(
            TrackingHubMethods.DeliveryOffered,
            frame => channel.Writer.TryWrite(frame));

        await connection.StartAsync(cancellationToken);

        return new TrackedConnection(connection, channel.Reader);
    }

    /// <summary>A connected hub client whose <c>DeliveryOffered</c> frames stream into a channel.</summary>
    private sealed class TrackedConnection(HubConnection connection, ChannelReader<DeliveryOfferFrame> frames)
        : IAsyncDisposable
    {
        /// <summary>
        /// Blocks until this connection is provably in its <c>user:{sub}</c> group. <c>StartAsync</c>
        /// returns once the client reads the handshake response, which the server writes
        /// <em>before</em> running <c>TrackingHub.OnConnectedAsync</c> — where the group is joined —
        /// so an event published the instant it returns can be fanned out to a group this connection
        /// has not joined yet. Same probe loop, and same reasoning, as <c>DashboardFanOutTests</c>:
        /// membership is not observable from the client, so the fan-out is the probe. Each probe
        /// carries its own delivery id, and every assertion below matches on the id it cares about,
        /// so a probe frame still in flight is never mistaken for the frame under test.
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
        public async Task<DeliveryOfferFrame> ReadNextAsync(
            Func<DeliveryOfferFrame, bool> matches,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReceiveTimeout);

            while (true)
            {
                DeliveryOfferFrame frame = await frames.ReadAsync(timeout.Token);

                if (matches(frame))
                {
                    return frame;
                }
            }
        }

        /// <summary>
        /// Asserts no frame matching <paramref name="matches"/> arrives within the silence window.
        /// Non-matching frames are ignored, so a probe frame in flight cannot masquerade as the leak
        /// being tested for.
        /// </summary>
        public async Task AssertNoFrameAsync(Func<DeliveryOfferFrame, bool> matches)
        {
            using var timeout = new CancellationTokenSource(SilenceWindow);

            Func<Task> read = async () =>
            {
                while (true)
                {
                    DeliveryOfferFrame frame = await frames.ReadAsync(timeout.Token);

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
