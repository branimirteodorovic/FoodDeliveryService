using System.Threading.Channels;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.RealTime.IntegrationTests.Abstractions;
using Microsoft.AspNetCore.SignalR.Client;

namespace FoodDeliveryService.Modules.RealTime.IntegrationTests.Tracking;

/// <summary>
/// Milestone B: an Orders lifecycle event published on the bus becomes a live <c>OrderStatusChanged</c>
/// frame on the owning customer's timeline — and only theirs. Drives the real path end to end:
/// IEventBus → RabbitMQ → the RealTime direct consumer → SignalR group → the connected client.
/// </summary>
public class OrderStatusFanOutTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task PublishedOrderStatus_IsPushedToTheOwningCustomer()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        await PublishAsync(OrderAccepted(orderId, Factory.TestUserId, restaurantId), ct);

        OrderStatusFrame frame = await client.ReadNextAsync(ct);
        frame.OrderId.Should().Be(orderId);
        frame.Status.Should().Be(OrderStatuses.Accepted);

        // The routing map was warmed so Milestone C can resolve this order's customer.
        OrderRoutingEntry? routing = await GetOrderRoutingAsync(orderId, ct);
        routing.Should().Be(new OrderRoutingEntry(Factory.TestUserId, restaurantId));
    }

    [Fact]
    public async Task OrderStatus_ForAnotherCustomer_IsNotPushedToThisCustomer()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        // Same event shape, but addressed to a different customer id — the connected client is in
        // user:{TestUserId}, never a broadcast target, so nothing should reach it.
        await PublishAsync(OrderAccepted(Guid.NewGuid(), customerId: Guid.NewGuid(), Guid.NewGuid()), ct);

        await client.AssertNoFrameAsync();
    }

    [Fact]
    public async Task TheFullLifecycleSequence_ArrivesAsOrderedTimelineFrames()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        var orderId = Guid.NewGuid();
        Guid customerId = Factory.TestUserId;
        var restaurantId = Guid.NewGuid();

        // Each transition rides its own consumer/queue, so publish-then-await each frame in turn to
        // observe a deterministic timeline (rather than racing four independent queues).
        var statuses = new List<string>();

        await PublishAsync(OrderPlaced(orderId, customerId, restaurantId), ct);
        statuses.Add((await client.ReadNextAsync(ct)).Status);

        await PublishAsync(OrderAccepted(orderId, customerId, restaurantId), ct);
        statuses.Add((await client.ReadNextAsync(ct)).Status);

        await PublishAsync(OrderReadyForPickup(orderId, customerId, restaurantId), ct);
        statuses.Add((await client.ReadNextAsync(ct)).Status);

        await PublishAsync(OrderCancelled(orderId, customerId, restaurantId), ct);
        statuses.Add((await client.ReadNextAsync(ct)).Status);

        statuses.Should().Equal(
            OrderStatuses.Placed, OrderStatuses.Accepted, OrderStatuses.ReadyForPickup, OrderStatuses.Cancelled);
    }

    /// <summary>Opens an authenticated socket as the seeded customer and streams its status frames.</summary>
    private async Task<TrackedConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        string accessToken = await GetAccessTokenAsync();
        var connection = BuildHubConnection(accessToken);

        var channel = Channel.CreateUnbounded<OrderStatusFrame>();
        connection.On<OrderStatusFrame>(
            TrackingHubMethods.OrderStatusChanged,
            frame => channel.Writer.TryWrite(frame));

        await connection.StartAsync(cancellationToken);

        return new TrackedConnection(connection, channel.Reader);
    }

    private static OrderPlacedIntegrationEvent OrderPlaced(Guid orderId, Guid customerId, Guid restaurantId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, customerId, restaurantId, subtotal: 42m, placedOnUtc: DateTime.UtcNow);

    private static OrderAcceptedIntegrationEvent OrderAccepted(Guid orderId, Guid customerId, Guid restaurantId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, customerId, restaurantId, acceptedOnUtc: DateTime.UtcNow);

    private static OrderReadyForPickupIntegrationEvent OrderReadyForPickup(Guid orderId, Guid customerId, Guid restaurantId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, customerId, restaurantId,
            restaurantLatitude: 1, restaurantLongitude: 2,
            deliveryStreet: "1 Main St", deliveryCity: "Town", deliveryPostalCode: "0000", deliveryCountry: "Country",
            deliveryNotes: null, deliveryLatitude: 3, deliveryLongitude: 4, subtotal: 42m, placedOnUtc: DateTime.UtcNow);

    private static OrderCancelledIntegrationEvent OrderCancelled(Guid orderId, Guid customerId, Guid restaurantId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, customerId, restaurantId, cancelledOnUtc: DateTime.UtcNow);

    /// <summary>A connected hub client whose <c>OrderStatusChanged</c> frames stream into a channel.</summary>
    private sealed class TrackedConnection(HubConnection connection, ChannelReader<OrderStatusFrame> frames) : IAsyncDisposable
    {
        public async Task<OrderStatusFrame> ReadNextAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReceiveTimeout);

            return await frames.ReadAsync(timeout.Token);
        }

        public async Task AssertNoFrameAsync()
        {
            using var timeout = new CancellationTokenSource(SilenceWindow);

            Func<Task> read = async () => await frames.ReadAsync(timeout.Token);

            // Nothing is ever written, so the read is cancelled once the silence window elapses.
            await read.Should().ThrowAsync<OperationCanceledException>();
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
