using System.Threading.Channels;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.RealTime.IntegrationTests.Abstractions;
using Microsoft.AspNetCore.SignalR.Client;

namespace FoodDeliveryService.Modules.RealTime.IntegrationTests.Tracking;

/// <summary>
/// Milestone C: a driver assignment binds driver→order/customer, Delivery's own lifecycle events
/// become live <c>OrderStatusChanged</c> frames, and positions PUBLISHed on Delivery's Redis
/// pub/sub channel are forwarded as <c>DriverLocationChanged</c> to the tracking customer — but only
/// while a binding is active. Drives the real path: Redis PUBLISH → the RealTime subscriber (for
/// locations) and IEventBus → RabbitMQ → direct consumers (for the lifecycle events) → SignalR
/// group → the connected client.
/// </summary>
public class DriverLocationFanOutTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task DriverAssigned_PushesStatusFrameAndBindsTheDriverForLocationTracking()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();

        // Warm the routing map first, exactly as Milestone B does on every real order transition.
        await PublishAsync(OrderAccepted(orderId, Factory.TestUserId, restaurantId), ct);
        await client.ReadNextStatusAsync(ct);

        await PublishAsync(DriverAssigned(orderId, driverId), ct);

        OrderStatusFrame frame = await client.ReadNextStatusAsync(ct);
        frame.OrderId.Should().Be(orderId);
        frame.Status.Should().Be(OrderStatuses.DriverAssigned);
        frame.DriverName.Should().Be("Alex Rivera");
        frame.DriverVehicle.Should().Be("Bike");

        DriverBinding? binding = await GetDriverBindingAsync(driverId, ct);
        binding.Should().Be(new DriverBinding(orderId, Factory.TestUserId));
    }

    [Fact]
    public async Task DriverAssigned_WithNoWarmedRoutingEntry_DropsTheFrame()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        // No Orders event published first, so rt:order:{orderId} was never warmed — best-effort drop.
        await PublishAsync(DriverAssigned(Guid.NewGuid(), Guid.NewGuid()), ct);

        await client.AssertNoStatusFrameAsync();
    }

    [Fact]
    public async Task OrderPickedUp_PushesOutForDeliveryFrame()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        var orderId = Guid.NewGuid();
        await PublishAsync(OrderAccepted(orderId, Factory.TestUserId, Guid.NewGuid()), ct);
        await client.ReadNextStatusAsync(ct);

        await PublishAsync(OrderPickedUp(orderId), ct);

        OrderStatusFrame frame = await client.ReadNextStatusAsync(ct);
        frame.OrderId.Should().Be(orderId);
        frame.Status.Should().Be(OrderStatuses.OutForDelivery);
    }

    [Fact]
    public async Task OrderDelivered_PushesDeliveredFrameAndClearsTheDriverBinding()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        var orderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        await PublishAsync(OrderAccepted(orderId, Factory.TestUserId, Guid.NewGuid()), ct);
        await client.ReadNextStatusAsync(ct);
        await PublishAsync(DriverAssigned(orderId, driverId), ct);
        await client.ReadNextStatusAsync(ct);

        await PublishAsync(OrderDelivered(orderId, driverId), ct);

        OrderStatusFrame frame = await client.ReadNextStatusAsync(ct);
        frame.Status.Should().Be(OrderStatuses.Delivered);

        (await GetDriverBindingAsync(driverId, ct)).Should().BeNull();
    }

    [Fact]
    public async Task OrderCancelled_ClearsAnExistingDriverBinding()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        var orderId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        await PublishAsync(OrderAccepted(orderId, Factory.TestUserId, restaurantId), ct);
        await client.ReadNextStatusAsync(ct);
        await PublishAsync(DriverAssigned(orderId, driverId), ct);
        await client.ReadNextStatusAsync(ct);

        await PublishAsync(OrderCancelled(orderId, Factory.TestUserId, restaurantId), ct);
        await client.ReadNextStatusAsync(ct);

        (await GetDriverBindingAsync(driverId, ct)).Should().BeNull();
    }

    [Fact]
    public async Task PublishedLocation_ForABoundDriver_IsPushedToTheTrackingCustomer()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        var orderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        await PublishAsync(OrderAccepted(orderId, Factory.TestUserId, Guid.NewGuid()), ct);
        await client.ReadNextStatusAsync(ct);
        await PublishAsync(DriverAssigned(orderId, driverId), ct);
        await client.ReadNextStatusAsync(ct);

        var recordedOnUtc = DateTime.UtcNow;
        await PublishDriverLocationAsync(driverId, 51.5, -0.12, recordedOnUtc);

        DriverLocationFrame frame = await client.ReadNextLocationAsync(ct);
        frame.OrderId.Should().Be(orderId);
        frame.DriverId.Should().Be(driverId);
        frame.Latitude.Should().Be(51.5);
        frame.Longitude.Should().Be(-0.12);
    }

    [Fact]
    public async Task PublishedLocation_ForAnUnboundDriver_IsNotPushed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        // No DriverAssigned event was ever published for this driver — no binding exists.
        await PublishDriverLocationAsync(Guid.NewGuid(), 1, 2, DateTime.UtcNow);

        await client.AssertNoLocationFrameAsync();
    }

    [Fact]
    public async Task PublishedLocation_AfterOrderDelivered_IsNoLongerPushed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using TrackedConnection client = await ConnectAsync(ct);

        var orderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        await PublishAsync(OrderAccepted(orderId, Factory.TestUserId, Guid.NewGuid()), ct);
        await client.ReadNextStatusAsync(ct);
        await PublishAsync(DriverAssigned(orderId, driverId), ct);
        await client.ReadNextStatusAsync(ct);
        await PublishAsync(OrderDelivered(orderId, driverId), ct);
        await client.ReadNextStatusAsync(ct);

        await PublishDriverLocationAsync(driverId, 1, 2, DateTime.UtcNow);

        await client.AssertNoLocationFrameAsync();
    }

    /// <summary>Opens an authenticated socket as the seeded customer and streams both frame kinds.</summary>
    private async Task<TrackedConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        string accessToken = await GetAccessTokenAsync();
        var connection = BuildHubConnection(accessToken);

        var statusChannel = Channel.CreateUnbounded<OrderStatusFrame>();
        connection.On<OrderStatusFrame>(
            TrackingHubMethods.OrderStatusChanged,
            frame => statusChannel.Writer.TryWrite(frame));

        var locationChannel = Channel.CreateUnbounded<DriverLocationFrame>();
        connection.On<DriverLocationFrame>(
            TrackingHubMethods.DriverLocationChanged,
            frame => locationChannel.Writer.TryWrite(frame));

        await connection.StartAsync(cancellationToken);

        return new TrackedConnection(connection, statusChannel.Reader, locationChannel.Reader);
    }

    private static OrderAcceptedIntegrationEvent OrderAccepted(Guid orderId, Guid customerId, Guid restaurantId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, customerId, restaurantId, acceptedOnUtc: DateTime.UtcNow);

    private static OrderCancelledIntegrationEvent OrderCancelled(Guid orderId, Guid customerId, Guid restaurantId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, customerId, restaurantId, cancelledOnUtc: DateTime.UtcNow);

    private static DriverAssignedIntegrationEvent DriverAssigned(Guid orderId, Guid driverId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, deliveryId: Guid.NewGuid(), driverId,
            driverFirstName: "Alex", driverLastName: "Rivera", vehicleType: "Bike", assignedOnUtc: DateTime.UtcNow);

    private static OrderPickedUpIntegrationEvent OrderPickedUp(Guid orderId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, deliveryId: Guid.NewGuid(), driverId: Guid.NewGuid(), pickedUpOnUtc: DateTime.UtcNow);

    private static OrderDeliveredIntegrationEvent OrderDelivered(Guid orderId, Guid driverId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, orderId, deliveryId: Guid.NewGuid(), driverId, deliveredOnUtc: DateTime.UtcNow);

    /// <summary>A connected hub client whose status and location frames stream into their own channels.</summary>
    private sealed class TrackedConnection(
        HubConnection connection,
        ChannelReader<OrderStatusFrame> statusFrames,
        ChannelReader<DriverLocationFrame> locationFrames) : IAsyncDisposable
    {
        public async Task<OrderStatusFrame> ReadNextStatusAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReceiveTimeout);

            return await statusFrames.ReadAsync(timeout.Token);
        }

        public async Task<DriverLocationFrame> ReadNextLocationAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReceiveTimeout);

            return await locationFrames.ReadAsync(timeout.Token);
        }

        public Task AssertNoStatusFrameAsync() => AssertSilentAsync(statusFrames);

        public Task AssertNoLocationFrameAsync() => AssertSilentAsync(locationFrames);

        private static async Task AssertSilentAsync<T>(ChannelReader<T> reader)
        {
            using var timeout = new CancellationTokenSource(SilenceWindow);

            Func<Task> read = async () => await reader.ReadAsync(timeout.Token);

            // Nothing is ever written, so the read is cancelled once the silence window elapses.
            await read.Should().ThrowAsync<OperationCanceledException>();
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
