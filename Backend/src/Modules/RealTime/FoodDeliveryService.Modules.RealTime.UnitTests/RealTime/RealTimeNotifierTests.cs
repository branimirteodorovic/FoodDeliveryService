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
}
