using AwesomeAssertions;
using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;
using FoodDeliveryService.Modules.Notifications.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Notifications.UnitTests.RecipientUsers;

public class RecipientUsersTests : BaseTest
{
    [Fact]
    public void Create_ShouldSetFieldsFromUserReplica()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = Faker.Person.Email;
        var firstName = Faker.Person.FirstName;
        var lastName = Faker.Person.LastName;

        // Act
        var recipient = RecipientUser.Create(userId, email, firstName, lastName);

        // Assert
        recipient.Id.Should().Be(userId);
        recipient.Email.Should().Be(email);
        recipient.FirstName.Should().Be(firstName);
        recipient.LastName.Should().Be(lastName);
    }

    [Fact]
    public void Create_ShouldRaiseNoDomainEvents_BecauseReplicaIsAProjection()
    {
        // Arrange & Act
        RecipientUser recipient = CreateRecipientUser();

        // Assert
        recipient.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_ShouldReplaceNameFields()
    {
        // Arrange
        RecipientUser recipient = CreateRecipientUser();
        var newFirstName = Faker.Person.FirstName;
        var newLastName = Faker.Person.LastName;

        // Act
        recipient.Update(newFirstName, newLastName);

        // Assert
        recipient.FirstName.Should().Be(newFirstName);
        recipient.LastName.Should().Be(newLastName);
    }

    [Fact]
    public void Update_ShouldNotChangeIdOrEmail()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = Faker.Person.Email;
        var recipient = RecipientUser.Create(userId, email, Faker.Person.FirstName, Faker.Person.LastName);

        // Act
        recipient.Update(Faker.Person.FirstName, Faker.Person.LastName);

        // Assert
        recipient.Id.Should().Be(userId);
        recipient.Email.Should().Be(email);
    }

    [Fact]
    public void Update_ShouldRaiseNoDomainEvents_BecauseReplicaIsAProjection()
    {
        // Arrange
        RecipientUser recipient = CreateRecipientUser();

        // Act
        recipient.Update(Faker.Person.FirstName, Faker.Person.LastName);

        // Assert
        recipient.DomainEvents.Should().BeEmpty();
    }

    private static RecipientUser CreateRecipientUser()
    {
        return RecipientUser.Create(
            Guid.NewGuid(),
            Faker.Person.Email,
            Faker.Person.FirstName,
            Faker.Person.LastName);
    }
}
