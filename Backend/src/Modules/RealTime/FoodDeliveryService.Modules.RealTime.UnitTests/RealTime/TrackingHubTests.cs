using System.Security.Claims;
using AwesomeAssertions;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.RealTime.Presentation.Tracking;
using FoodDeliveryService.Modules.RealTime.UnitTests.RealTime.Fakes;

namespace FoodDeliveryService.Modules.RealTime.UnitTests.RealTime;

public class TrackingHubTests
{
    private const string ConnectionId = "connection-1";

    [Fact]
    public async Task OnConnectedAsync_ShouldJoinTheCallersOwnUserGroup()
    {
        var userId = Guid.NewGuid();
        var groups = new RecordingGroupManager();
        using TrackingHub hub = CreateHub(WithSub(userId), groups);

        await hub.OnConnectedAsync();

        groups.Added.Should().ContainSingle()
            .Which.Should().Be((ConnectionId, GroupNames.User(userId)));
    }

    [Fact]
    public async Task OnConnectedAsync_ShouldJoinNoOtherGroup()
    {
        var userId = Guid.NewGuid();
        var groups = new RecordingGroupManager();
        using TrackingHub hub = CreateHub(WithSub(userId), groups);

        await hub.OnConnectedAsync();

        // Exactly one group, and it is derived from the caller's own claim — never a restaurant or
        // the support group, and never an id the client supplied.
        groups.Added.Should().HaveCount(1);
        groups.Added.Should().OnlyContain(g => g.GroupName == GroupNames.User(userId));
    }

    [Fact]
    public async Task OnConnectedAsync_ShouldRejectAPrincipalWithoutASubClaim()
    {
        var groups = new RecordingGroupManager();
        // An authenticated principal whose "sub" was never resolved (no CustomClaimsTransformation).
        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test"));
        using TrackingHub hub = CreateHub(principal, groups);

        await Assert.ThrowsAsync<Common.Application.Exceptions.ApplicationException>(hub.OnConnectedAsync);

        groups.Added.Should().BeEmpty();
    }

    private static TrackingHub CreateHub(ClaimsPrincipal principal, RecordingGroupManager groups) =>
        new()
        {
            Context = new FakeHubCallerContext(ConnectionId, principal),
            Groups = groups
        };

    private static ClaimsPrincipal WithSub(Guid userId) =>
        new(new ClaimsIdentity([new Claim("sub", userId.ToString())], authenticationType: "Test"));
}
