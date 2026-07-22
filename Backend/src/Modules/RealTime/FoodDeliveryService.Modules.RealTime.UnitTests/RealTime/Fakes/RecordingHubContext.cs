using FoodDeliveryService.Modules.RealTime.Presentation.Tracking;
using Microsoft.AspNetCore.SignalR;

namespace FoodDeliveryService.Modules.RealTime.UnitTests.RealTime.Fakes;

/// <summary>
/// Hand-rolled <see cref="IHubContext{THub}"/> test double (the codebase uses no mocking library)
/// that captures which group each server→client message was sent to and with what payload — enough
/// to prove the notifier fans a frame out to exactly one <c>user:{id}</c> group and nowhere else.
/// Only <see cref="IHubClients.Group(string)"/> is exercised; the rest of the fan-out surface throws
/// so a test that accidentally used another target fails loudly.
/// </summary>
internal sealed class RecordingHubContext : IHubContext<TrackingHub>
{
    private readonly RecordingHubClients _clients = new();

    public IHubClients Clients => _clients;

    public IGroupManager Groups { get; } = new RecordingGroupManager();

    /// <summary>The proxy for a group, or null if nothing was ever sent to it.</summary>
    public RecordingClientProxy? ProxyFor(string groupName) =>
        _clients.Proxies.GetValueOrDefault(groupName);

    internal sealed class RecordingHubClients : IHubClients
    {
        public Dictionary<string, RecordingClientProxy> Proxies { get; } = [];

        public IClientProxy Group(string groupName)
        {
            if (!Proxies.TryGetValue(groupName, out RecordingClientProxy? proxy))
            {
                proxy = new RecordingClientProxy();
                Proxies[groupName] = proxy;
            }

            return proxy;
        }

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }
}

/// <summary>Records every server→client send so a test can assert the method name and payload.</summary>
internal sealed class RecordingClientProxy : IClientProxy
{
    public List<(string Method, object?[] Args)> Sent { get; } = [];

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        Sent.Add((method, args));
        return Task.CompletedTask;
    }
}
