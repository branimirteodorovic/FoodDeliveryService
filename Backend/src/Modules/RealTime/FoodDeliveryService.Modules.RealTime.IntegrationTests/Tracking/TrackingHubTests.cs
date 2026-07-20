using AwesomeAssertions;
using FoodDeliveryService.Modules.RealTime.IntegrationTests.Abstractions;
using Microsoft.AspNetCore.SignalR.Client;

namespace FoodDeliveryService.Modules.RealTime.IntegrationTests.Tracking;

public class TrackingHubTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Connect_WithAValidToken_EstablishesTheSocket()
    {
        string accessToken = await GetAccessTokenAsync();
        await using HubConnection connection = BuildHubConnection(accessToken);

        await connection.StartAsync(TestContext.Current.CancellationToken);

        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task Connect_WithoutAToken_IsRejectedAtTheHandshake()
    {
        await using HubConnection connection = BuildHubConnection(accessToken: null);

        Func<Task> connect = () => connection.StartAsync(TestContext.Current.CancellationToken);

        await connect.Should().ThrowAsync<Exception>();
        connection.State.Should().Be(HubConnectionState.Disconnected);
    }

    [Fact]
    public async Task Connect_WithAnInvalidToken_IsRejectedAtTheHandshake()
    {
        await using HubConnection connection = BuildHubConnection("not-a-real-jwt");

        Func<Task> connect = () => connection.StartAsync(TestContext.Current.CancellationToken);

        await connect.Should().ThrowAsync<Exception>();
        connection.State.Should().Be(HubConnectionState.Disconnected);
    }

    [Fact]
    public async Task Connection_CanBeReestablishedAfterADisconnect()
    {
        // Milestone A has no server→client broadcast to assert the re-joined group directly; the
        // group re-join on every (re)connect is covered by the OnConnectedAsync unit test. Here we
        // prove the connection substrate itself re-establishes cleanly — which is what runs
        // OnConnectedAsync again — mirroring SignalR's native withAutomaticReconnect behaviour.
        string accessToken = await GetAccessTokenAsync();
        await using HubConnection connection = BuildHubConnection(accessToken, withAutomaticReconnect: true);

        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.StopAsync(TestContext.Current.CancellationToken);

        await connection.StartAsync(TestContext.Current.CancellationToken);

        connection.State.Should().Be(HubConnectionState.Connected);
    }
}
