using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;
using FoodDeliveryService.Modules.Notifications.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Notifications.IntegrationTests.RecipientUsers;

public class RecipientUserReplicaTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task UserRegistered_Should_MaterializeRecipientUserReplica()
    {
        // Arrange
        var userId = Guid.NewGuid();
        string email = UniqueEmail();
        string firstName = Faker.Name.FirstName();
        string lastName = Faker.Name.LastName();

        // Act — publish the Users event onto the broker; Notifications' consumer writes it to its
        // inbox and ProcessInboxJob upserts the local RecipientUser replica.
        await Factory.PublishAsync(
            new UserRegisteredIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                userId,
                email,
                firstName,
                lastName,
                CustomerRoles),
            TestContext.Current.CancellationToken);

        // Assert
        RecipientUser replica = await WaitForRecipientReplicaAsync(userId, TestContext.Current.CancellationToken);

        replica.Id.Should().Be(userId);
        replica.Email.Should().Be(email);
        replica.FirstName.Should().Be(firstName);
        replica.LastName.Should().Be(lastName);
    }

    [Fact]
    public async Task UserProfileUpdated_Should_SyncRecipientUserName()
    {
        // Arrange — seed the replica, then update the profile with new names.
        SeededRecipient recipient = await SeedRecipientAsync(TestContext.Current.CancellationToken);

        string newFirstName = Faker.Name.FirstName();
        string newLastName = Faker.Name.LastName();

        // Act
        await Factory.PublishAsync(
            new UserProfileUpdatedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                recipient.UserId,
                newFirstName,
                newLastName),
            TestContext.Current.CancellationToken);

        // Assert — poll until the replica reflects the new names (a fresh scope per attempt avoids a
        // stale DbContext cache), not merely until any row exists.
        Result<RecipientUser> synced = await Poller.WaitAsync<RecipientUser>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

                var repository = scope.ServiceProvider.GetRequiredService<IRecipientUserRepository>();

                RecipientUser? recipientUser =
                    await repository.GetAsync(recipient.UserId, TestContext.Current.CancellationToken);

                return recipientUser is not null &&
                       recipientUser.FirstName == newFirstName &&
                       recipientUser.LastName == newLastName
                    ? Result.Success(recipientUser)
                    : Result.Failure<RecipientUser>(Error.NullValue);
            });

        synced.IsSuccess.Should().BeTrue("the profile update should sync onto the recipient replica");
        synced.Value.FirstName.Should().Be(newFirstName);
        synced.Value.LastName.Should().Be(newLastName);
        // The profile-update event carries no email, so the replica's address is left untouched.
        synced.Value.Email.Should().Be(recipient.Email);
    }
}
