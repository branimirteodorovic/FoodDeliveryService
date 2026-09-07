using System.Data.Common;
using AwesomeAssertions;
using Bogus;
using FoodDeliveryService.Common.Application.Data;
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

        // Only read on failure — on the happy path this would be one wasted query per seeded user.
        string inbox = replica.IsFailure ? await DescribeInboxAsync(cancellationToken) : string.Empty;

        replica.IsSuccess.Should().BeTrue(
            "the RecipientUser replica should be materialized from the user event. Inbox state: {0}",
            inbox);

        return replica.Value;
    }

    /// <summary>
    /// Dumps the module's inbox rows. Used only to explain a timeout: whether the event never
    /// reached the inbox at all (lost between publish and consume) or reached it and was never
    /// dispatched (or dispatched and failed) is the difference between a broker-side and a
    /// job-side fault, and the assertion message is the only place CI can tell us which.
    /// </summary>
    private async Task<string> DescribeInboxAsync(CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id, type, processed_on_utc, error
            FROM inbox_messages
            ORDER BY occurred_on_utc
            """;

        try
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

            var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

            await using DbConnection connection = await connectionFactory.OpenConnectionAsync();

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;

            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            var rows = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(
                    $"[{reader.GetGuid(0)} {reader.GetString(1)} " +
                    $"processed={(await reader.IsDBNullAsync(2, cancellationToken) ? "no" : "yes")} " +
                    $"error={(await reader.IsDBNullAsync(3, cancellationToken) ? "none" : reader.GetString(3))}]");
            }

            return rows.Count == 0 ? "the inbox is empty" : string.Join(" ", rows);
        }
#pragma warning disable CA1031 // Diagnostics only: a broken dump must not replace the real failure.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return $"the inbox could not be read: {exception.Message}";
        }
    }

    protected sealed record SeededRecipient(Guid UserId, string Email, string FirstName, string LastName);
}
