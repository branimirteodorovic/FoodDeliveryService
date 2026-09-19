using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Orders.GetOrder;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Orders;

public class OrderLifecycleTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task ManagerTransitions_ShouldAdvanceOrderToReadyForPickup_WhenAppliedInOrder()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        PlacedOrder order = await PlaceOrderAsync(client);

        // Act — the full manager-driven happy path
        HttpResponseMessage accept = await client.PostAsync(
            $"orders/{order.OrderId}/accept", null, TestContext.Current.CancellationToken);
        HttpResponseMessage preparing = await client.PostAsync(
            $"orders/{order.OrderId}/preparing", null, TestContext.Current.CancellationToken);
        HttpResponseMessage ready = await client.PostAsync(
            $"orders/{order.OrderId}/ready", null, TestContext.Current.CancellationToken);

        // Assert
        accept.StatusCode.Should().Be(HttpStatusCode.NoContent);
        preparing.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ready.StatusCode.Should().Be(HttpStatusCode.NoContent);

        OrderResponse? persisted = await GetOrderAsync(client, order.OrderId);
        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(OrderStatus.ReadyForPickup);
    }

    [Fact]
    public async Task Ready_ShouldReturnBadRequest_WhenOrderWasNeverAccepted()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        PlacedOrder order = await PlaceOrderAsync(client);

        // Act — jump straight to ready from Pending (illegal transition)
        HttpResponseMessage response = await client.PostAsync(
            $"orders/{order.OrderId}/ready", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Accept_ShouldReturnUnauthorized_WhenNoTokenIsPresent()
    {
        // Act — HttpClient has no bearer token attached
        HttpResponseMessage response = await HttpClient.PostAsync(
            $"orders/{Guid.NewGuid()}/accept", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Accept_ShouldPublishAcceptedEventThroughTheOutbox()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        PlacedOrder order = await PlaceOrderAsync(client);

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"orders/{order.OrderId}/accept", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<bool> processed = await WaitForProcessedOutboxEventAsync(nameof(OrderAcceptedDomainEvent));
        processed.IsSuccess.Should().BeTrue("accepting an order must raise its event and the outbox must publish it");
    }

    /// <summary>
    /// The Accepted → Preparing step goes out on the broker like every other transition. It did not
    /// always: the handler was metrics-only until RealTime needed the kitchen step for the customer's
    /// live timeline. A processed, error-free outbox row is the whole contract — it means the
    /// aggregate raised the event, the interceptor committed it with the state change, and the
    /// domain-event handler published OrderPreparingIntegrationEvent without throwing.
    /// </summary>
    [Fact]
    public async Task Preparing_ShouldPublishPreparingEventThroughTheOutbox()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        PlacedOrder order = await PlaceOrderAsync(client);

        HttpResponseMessage accept = await client.PostAsync(
            $"orders/{order.OrderId}/accept", null, TestContext.Current.CancellationToken);
        accept.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"orders/{order.OrderId}/preparing", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<bool> processed = await WaitForProcessedOutboxEventAsync(nameof(OrderPreparingDomainEvent));
        processed.IsSuccess.Should().BeTrue(
            "starting preparation must raise its event and the outbox must publish it");
    }

    private static async Task<OrderResponse?> GetOrderAsync(HttpClient client, Guid orderId)
    {
        HttpResponseMessage response = await client.GetAsync($"orders/{orderId}", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions, TestContext.Current.CancellationToken);
    }
}
