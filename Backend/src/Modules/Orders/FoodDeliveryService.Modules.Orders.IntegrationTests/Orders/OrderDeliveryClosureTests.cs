using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.Orders.Application.Orders.GetOrder;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Orders;

/// <summary>
/// Milestone F, Orders' half: the last two order transitions are driven entirely from the Delivery
/// service over the bus. Publishing OrderPickedUp/OrderDelivered (the Delivery contracts) drives the
/// order ReadyForPickup → OutForDelivery → Delivered through the inbox — the only callers of
/// Order.MarkOutForDelivery()/MarkDelivered(), with no HTTP call between the two services.
/// </summary>
public class OrderDeliveryClosureTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task OrderPickedUpThenDelivered_ShouldAdvanceOrder_ToOutForDeliveryThenDelivered()
    {
        // Arrange — take an order all the way to ReadyForPickup through the real manager endpoints.
        HttpClient client = await GetAuthenticatedHttpClientAsync();
        PlacedOrder order = await PlaceOrderAsync(client);

        await client.PostAsync($"orders/{order.OrderId}/accept", null, TestContext.Current.CancellationToken);
        await client.PostAsync($"orders/{order.OrderId}/preparing", null, TestContext.Current.CancellationToken);
        HttpResponseMessage ready = await client.PostAsync(
            $"orders/{order.OrderId}/ready", null, TestContext.Current.CancellationToken);
        ready.EnsureSuccessStatusCode();

        var eventBus = Factory.Services.GetRequiredService<IEventBus>();

        // Act — Delivery reports the driver collected the food.
        await eventBus.PublishAsync(
            new OrderPickedUpIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                order.OrderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        // Assert — the order advances to OutForDelivery via the inbox.
        Result<OrderStatus> outForDelivery = await WaitForOrderStatusAsync(client, order.OrderId, OrderStatus.OutForDelivery);
        outForDelivery.IsSuccess.Should().BeTrue("consuming OrderPickedUp must advance the order to OutForDelivery");

        // Act — Delivery reports the delivery complete.
        await eventBus.PublishAsync(
            new OrderDeliveredIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                order.OrderId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        // Assert — the order reaches its terminal Delivered state.
        Result<OrderStatus> delivered = await WaitForOrderStatusAsync(client, order.OrderId, OrderStatus.Delivered);
        delivered.IsSuccess.Should().BeTrue("consuming OrderDelivered must advance the order to Delivered");
    }

    private static Task<Result<OrderStatus>> WaitForOrderStatusAsync(HttpClient client, Guid orderId, OrderStatus expected) =>
        Poller.WaitAsync(
            TimeSpan.FromSeconds(30),
            async () =>
            {
                HttpResponseMessage response = await client.GetAsync(
                    $"orders/{orderId}", TestContext.Current.CancellationToken);

                response.EnsureSuccessStatusCode();

                OrderResponse? order = await response.Content.ReadFromJsonAsync<OrderResponse>(
                    JsonOptions, TestContext.Current.CancellationToken);

                return order?.Status == expected
                    ? Result.Success(expected)
                    : Result.Failure<OrderStatus>(Error.NullValue);
            });
}
