using AwesomeAssertions;
using Bogus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Notifications.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public abstract class BaseIntegrationTest(IntegrationTestWebAppFactory factory)
{
    protected static readonly Faker Faker = new();

    // A plain string[] (not a collection expression at the call site) for Roles — it must round-trip
    // through the inbox's Newtonsoft serialization intact (see the outbox serialization bug the Users
    // tests found). Hoisted to a field to satisfy CA1861 (no constant array arguments).
    protected static readonly string[] CustomerRoles = ["Customer"];

    protected IntegrationTestWebAppFactory Factory { get; } = factory;

    // Notifications' recipient replica is keyed on the Users service's UserId. A fresh Guid per test
    // keeps rows isolated, and a unique email keeps assertions unambiguous across the shared fixture.
    protected static string UniqueEmail() => $"notifications-tests+{Guid.NewGuid():N}@fooddeliveryservice.com";

    /// <summary>
    /// Publishes a <see cref="UserRegisteredIntegrationEvent"/> and waits for the resulting
    /// RecipientUser replica to materialize — the precondition for the order-confirmation flow, which
    /// can only resolve a recipient address once the replica exists.
    /// </summary>
    protected async Task<SeededRecipient> SeedRecipientAsync(CancellationToken cancellationToken = default)
    {
        var recipient = new SeededRecipient(
            Guid.NewGuid(),
            UniqueEmail(),
            Faker.Name.FirstName(),
            Faker.Name.LastName());

        await Factory.PublishAsync(
            new UserRegisteredIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                recipient.UserId,
                recipient.Email,
                recipient.FirstName,
                recipient.LastName,
                CustomerRoles),
            cancellationToken);

        await WaitForRecipientReplicaAsync(recipient.UserId, cancellationToken);

        return recipient;
    }

    /// <summary>
    /// Polls the module's own DI for the RecipientUser replica keyed on <paramref name="userId"/>,
    /// failing the test if it never materializes within the timeout.
    /// </summary>
    protected async Task<RecipientUser> WaitForRecipientReplicaAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        Result<RecipientUser> replica = await Poller.WaitAsync<RecipientUser>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

                var repository = scope.ServiceProvider.GetRequiredService<IRecipientUserRepository>();

                // RecipientUser? → Result<RecipientUser>: null converts to Failure(Error.NullValue),
                // which keeps the poller retrying until the replica is consumed from the bus.
                return await repository.GetAsync(userId, cancellationToken);
            });

        replica.IsSuccess.Should().BeTrue("the RecipientUser replica should be materialized from the user event");

        return replica.Value;
    }

    protected sealed record SeededRecipient(Guid UserId, string Email, string FirstName, string LastName);
}
