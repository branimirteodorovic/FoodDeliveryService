using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Orders;

public class OrderAuthorizationTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Accept_ShouldReturnForbidden_WhenCallerIsACustomer()
    {
        // Arrange — an order exists, placed by the Administrator...
        HttpClient adminClient = await GetAuthenticatedHttpClientAsync();
        PlacedOrder order = await PlaceOrderAsync(adminClient);

        // ...and a plain Customer (no orders:manage permission) obtains a token.
        string customerToken = await RegisterCustomerAndGetTokenAsync();
        HttpClient customerClient = Factory.CreateClient();
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        // Act — the customer tries to accept the order (a manager-only transition)
        HttpResponseMessage response = await customerClient.PostAsync(
            $"orders/{order.OrderId}/accept", null, TestContext.Current.CancellationToken);

        // Assert — denied by the permission policy before the handler runs
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
