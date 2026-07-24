using AwesomeAssertions;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime;
using FoodDeliveryService.Modules.RealTime.UnitTests.RealTime.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodDeliveryService.Modules.RealTime.UnitTests.RealTime;

public class RealTimeNotifierTests
{
    private static readonly DateTime OccurredOnUtc = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NotifyUserAsync_SendsTheFrameToTheCallersUserGroupOnly()
    {
        var userId = Guid.NewGuid();
        var frame = new OrderStatusFrame(Guid.NewGuid(), OrderStatuses.Accepted, OccurredOnUtc);
        var hubContext = new RecordingHubContext();
        var notifier = new RealTimeNotifier(hubContext, NullLogger<RealTimeNotifier>.Instance);

        await notifier.NotifyUserAsync(userId, frame, TestContext.Current.CancellationToken);

        RecordingClientProxy? proxy = hubContext.ProxyFor(GroupNames.User(userId));
        proxy.Should().NotBeNull();
        (string method, object?[] args) = proxy!.Sent.Should().ContainSingle().Subject;
        method.Should().Be(TrackingHubMethods.OrderStatusChanged);
        args.Should().ContainSingle().Which.Should().Be(frame);
    }

    [Fact]
    public async Task NotifyUserAsync_DoesNotSendToAnotherUsersGroup()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var frame = new OrderStatusFrame(Guid.NewGuid(), OrderStatuses.Placed, OccurredOnUtc);
        var hubContext = new RecordingHubContext();
        var notifier = new RealTimeNotifier(hubContext, NullLogger<RealTimeNotifier>.Instance);

        await notifier.NotifyUserAsync(userId, frame, TestContext.Current.CancellationToken);

        hubContext.ProxyFor(GroupNames.User(otherUserId)).Should().BeNull();
    }

    [Fact]
    public async Task NotifyUserAsync_DriverLocationFrame_SendsToTheCallersUserGroupOnly()
    {
        var userId = Guid.NewGuid();
        var frame = new DriverLocationFrame(Guid.NewGuid(), Guid.NewGuid(), 1.23, 4.56, OccurredOnUtc);
        var hubContext = new RecordingHubContext();
        var notifier = new RealTimeNotifier(hubContext, NullLogger<RealTimeNotifier>.Instance);

        await notifier.NotifyUserAsync(userId, frame, TestContext.Current.CancellationToken);

        RecordingClientProxy? proxy = hubContext.ProxyFor(GroupNames.User(userId));
        proxy.Should().NotBeNull();
        (string method, object?[] args) = proxy!.Sent.Should().ContainSingle().Subject;
        method.Should().Be(TrackingHubMethods.DriverLocationChanged);
        args.Should().ContainSingle().Which.Should().Be(frame);
    }

    [Fact]
    public async Task NotifyUserAsync_DriverLocationFrame_DoesNotSendToAnotherUsersGroup()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var frame = new DriverLocationFrame(Guid.NewGuid(), Guid.NewGuid(), 1.23, 4.56, OccurredOnUtc);
        var hubContext = new RecordingHubContext();
        var notifier = new RealTimeNotifier(hubContext, NullLogger<RealTimeNotifier>.Instance);

        await notifier.NotifyUserAsync(userId, frame, TestContext.Current.CancellationToken);

        hubContext.ProxyFor(GroupNames.User(otherUserId)).Should().BeNull();
    }

    [Fact]
    public async Task NotifyRestaurantAsync_SendsTheFrameToTheRestaurantGroupOnly()
    {
        var restaurantId = Guid.NewGuid();
        var otherRestaurantId = Guid.NewGuid();
        var frame = new RestaurantActivityFrame(Guid.NewGuid(), OrderStatuses.Placed, OccurredOnUtc);
        var hubContext = new RecordingHubContext();
        var notifier = new RealTimeNotifier(hubContext, NullLogger<RealTimeNotifier>.Instance);

        await notifier.NotifyRestaurantAsync(restaurantId, frame, TestContext.Current.CancellationToken);

        RecordingClientProxy? proxy = hubContext.ProxyFor(GroupNames.Restaurant(restaurantId));
        proxy.Should().NotBeNull();
        (string method, object?[] args) = proxy!.Sent.Should().ContainSingle().Subject;
        method.Should().Be(TrackingHubMethods.RestaurantActivity);
        args.Should().ContainSingle().Which.Should().Be(frame);
        hubContext.ProxyFor(GroupNames.Restaurant(otherRestaurantId)).Should().BeNull();
    }

    [Fact]
    public async Task NotifySupportAsync_SendsTheFrameToTheSupportGroupOnly()
    {
        var frame = new SupportActivityFrame(Guid.NewGuid(), Guid.NewGuid(), OrderStatuses.Accepted, OccurredOnUtc);
        var hubContext = new RecordingHubContext();
        var notifier = new RealTimeNotifier(hubContext, NullLogger<RealTimeNotifier>.Instance);

        await notifier.NotifySupportAsync(frame, TestContext.Current.CancellationToken);

        RecordingClientProxy? proxy = hubContext.ProxyFor(GroupNames.Support);
        proxy.Should().NotBeNull();
        (string method, object?[] args) = proxy!.Sent.Should().ContainSingle().Subject;
        method.Should().Be(TrackingHubMethods.SupportActivity);
        args.Should().ContainSingle().Which.Should().Be(frame);
    }
}
