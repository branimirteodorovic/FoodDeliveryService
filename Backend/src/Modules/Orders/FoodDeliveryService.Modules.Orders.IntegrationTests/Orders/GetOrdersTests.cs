using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Orders.Application.Orders.GetOrder;
using FoodDeliveryService.Modules.Orders.Application.Orders.GetOrders;
using FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Orders;

public class GetOrdersTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task GetOrder_ShouldReturnOrderWithServerPricedItems_WhenReadingById()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        PlacedOrder order = await PlaceOrderAsync(client, quantity: 2);

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"orders/{order.OrderId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        OrderResponse? body =
            await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Id.Should().Be(order.OrderId);
        body.CustomerId.Should().Be(Factory.TestUserId);
        body.Subtotal.Should().Be(order.ExpectedSubtotal);
        body.Items.Should().ContainSingle(item => item.MenuItemId == order.MenuItemId && item.Quantity == 2);
    }

    [Fact]
    public async Task GetOrders_ShouldReturnCallersOrders_WhenListing()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        PlacedOrder order = await PlaceOrderAsync(client);

        // Act
        HttpResponseMessage response = await client.GetAsync("orders", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        List<OrderSummaryResponse>? orders = await response.Content.ReadFromJsonAsync<List<OrderSummaryResponse>>(
            JsonOptions, TestContext.Current.CancellationToken);
        orders.Should().NotBeNull();
        orders!.Should().Contain(summary => summary.Id == order.OrderId);
    }

    [Fact]
    public async Task GetOrder_ShouldReturnNotFound_WhenOrderDoesNotExist()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        // Act
        HttpResponseMessage response = await client.GetAsync(
            $"orders/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
