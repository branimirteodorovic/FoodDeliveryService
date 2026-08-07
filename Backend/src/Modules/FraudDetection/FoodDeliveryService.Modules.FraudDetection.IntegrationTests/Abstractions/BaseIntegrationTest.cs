using AwesomeAssertions;
using Bogus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.FraudDetection.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public abstract class BaseIntegrationTest(IntegrationTestWebAppFactory factory)
{
    protected static readonly Faker Faker = new();

    // A plain string[] (not a collection expression at the call site) for Roles — it must round-trip
    // through the inbox's Newtonsoft serialization intact (see the outbox serialization bug the Users
    // tests found). Hoisted to a field to satisfy CA1861 (no constant array arguments).
    protected static readonly string[] CustomerRoles = ["Customer"];

    protected IntegrationTestWebAppFactory Factory { get; } = factory;

    /// <summary>
    /// Polls the module's own DI until the customer projection satisfies <paramref name="predicate"/>.
    /// The predicate — rather than "any row exists" — is what makes these assertions meaningful: the
    /// row is created by the first event about the customer, so waiting for its mere existence would
    /// pass long before the counters under test had moved. A fresh scope per attempt avoids reading a
    /// stale DbContext cache.
    /// </summary>
    protected async Task<CustomerBehaviour> WaitForCustomerAsync(
        Guid customerId,
        Func<CustomerBehaviour, bool> predicate,
        string because,
        CancellationToken cancellationToken = default)
    {
        Result<CustomerBehaviour> result = await Poller.WaitAsync<CustomerBehaviour>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

                var repository = scope.ServiceProvider.GetRequiredService<ICustomerBehavioursRepository>();

                CustomerBehaviour? behaviour = await repository.GetAsync(customerId, cancellationToken);

                return behaviour is not null && predicate(behaviour)
                    ? Result.Success(behaviour)
                    : Result.Failure<CustomerBehaviour>(Error.NullValue);
            });

        result.IsSuccess.Should().BeTrue(because);

        return result.Value;
    }

    protected async Task<DriverBehaviour> WaitForDriverAsync(
        Guid driverId,
        Func<DriverBehaviour, bool> predicate,
        string because,
        CancellationToken cancellationToken = default)
    {
        Result<DriverBehaviour> result = await Poller.WaitAsync<DriverBehaviour>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

                var repository = scope.ServiceProvider.GetRequiredService<IDriverBehavioursRepository>();

                DriverBehaviour? behaviour = await repository.GetAsync(driverId, cancellationToken);

                return behaviour is not null && predicate(behaviour)
                    ? Result.Success(behaviour)
                    : Result.Failure<DriverBehaviour>(Error.NullValue);
            });

        result.IsSuccess.Should().BeTrue(because);

        return result.Value;
    }

    protected async Task<OrderFact> WaitForOrderFactAsync(
        Guid orderId,
        Func<OrderFact, bool> predicate,
        string because,
        CancellationToken cancellationToken = default)
    {
        Result<OrderFact> result = await Poller.WaitAsync<OrderFact>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

                var repository = scope.ServiceProvider.GetRequiredService<IOrderFactsRepository>();

                OrderFact? fact = await repository.GetAsync(orderId, cancellationToken);

                return fact is not null && predicate(fact)
                    ? Result.Success(fact)
                    : Result.Failure<OrderFact>(Error.NullValue);
            });

        result.IsSuccess.Should().BeTrue(because);

        return result.Value;
    }
}
