using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Domain.Customers;
using FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Users.Presentation.Users;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.RegisterUsers;

public class RegisterUserTests : BaseIntegrationTest
{
    public RegisterUserTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task RegisterUser_Should_ReturnOkWithUserId_WhenRequestValid()
    {
        // Arrange — users/register is the anonymous self-registration path (no auth required).
        var request = new RegisterUser.Request
        {
            Email = UniqueEmail(),
            Password = StrongPassword,
            FirstName = Faker.Name.FirstName(),
            LastName = Faker.Name.LastName(),
        };

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/register",
            request,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        Guid userId = await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
        userId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RegisterUser_Should_ReturnBadRequest_WhenEmailInvalid()
    {
        // Arrange
        var request = new RegisterUser.Request
        {
            Email = "not-an-email",
            Password = StrongPassword,
            FirstName = Faker.Name.FirstName(),
            LastName = Faker.Name.LastName(),
        };

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/register",
            request,
            TestContext.Current.CancellationToken);

        // Assert — RegisterUserCommandValidator rejects the email before Identity is touched.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegisterUser_Should_PropagateCustomerReplicaToOrdersModule()
    {
        // Arrange
        var request = new RegisterUser.Request
        {
            Email = UniqueEmail(),
            Password = StrongPassword,
            FirstName = Faker.Name.FirstName(),
            LastName = Faker.Name.LastName(),
        };

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/register",
            request,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        Guid userId = await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);

        // The replica arrives asynchronously: Users outbox job (≤1s) publishes
        // UserRegisteredIntegrationEvent → RabbitMQ → Orders inbox job (≤1s) dispatches
        // UpsertCustomerCommand. Poll the Orders test host's own DI for the replica row — the Customer
        // replica is internal to Orders and has no read endpoint.
        Result<Customer> replicaResult = await Poller.WaitAsync<Customer>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope();

                var customerRepository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();

                // Customer? → Result<Customer>: null converts to Failure(Error.NullValue), which keeps
                // the poller retrying until the replica materializes.
                return await customerRepository.GetAsync(userId, TestContext.Current.CancellationToken);
            });

        // Assert
        replicaResult.IsSuccess.Should().BeTrue("the customer replica should be consumed by the Orders module");

        Customer replica = replicaResult.Value;
        replica.Id.Should().Be(userId);
        replica.Email.Should().Be(request.Email);
        replica.FirstName.Should().Be(request.FirstName);
        replica.LastName.Should().Be(request.LastName);
    }
}
