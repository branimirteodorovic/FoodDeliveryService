using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Orders.Application.Orders.GetOrder;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Orders;

public class CancelOrderTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Cancel_ShouldMarkOrderCancelled_WhenStillPending()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        PlacedOrder order = await PlaceOrderAsync(client);

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"orders/{order.OrderId}/cancel", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage read = await client.GetAsync(
            $"orders/{order.OrderId}", TestContext.Current.CancellationToken);
        read.EnsureSuccessStatusCode();

        OrderResponse? persisted =
            await read.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions, TestContext.Current.CancellationToken);
        persisted!.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_ShouldReturnBadRequest_WhenOrderIsAlreadyPreparing()
    {
        // Arrange — advance past the point a customer may back out
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        PlacedOrder order = await PlaceOrderAsync(client);

        await client.PostAsync($"orders/{order.OrderId}/accept", null, TestContext.Current.CancellationToken);
        await client.PostAsync($"orders/{order.OrderId}/preparing", null, TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"orders/{order.OrderId}/cancel", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
