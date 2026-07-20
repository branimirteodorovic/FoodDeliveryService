using Microsoft.AspNetCore.SignalR;

namespace FoodDeliveryService.Modules.RealTime.UnitTests.RealTime.Fakes;

/// <summary>
/// Hand-rolled <see cref="IGroupManager"/> test double (the codebase uses no mocking library) that
/// records every group a connection is added to, so a test can assert the hub joins exactly the
/// groups it should and nothing else.
/// </summary>
internal sealed class RecordingGroupManager : IGroupManager
{
    public List<(string ConnectionId, string GroupName)> Added { get; } = [];

    public List<(string ConnectionId, string GroupName)> Removed { get; } = [];

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Added.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Removed.Add((connectionId, groupName));
        return Task.CompletedTask;
    }
}
