using AwesomeAssertions;
using FoodDeliveryService.Modules.Restaurants.Domain.Managers;
using FoodDeliveryService.Modules.Restaurants.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Restaurants.UnitTests.Managers;

public class RestaurantManagerTests : BaseTest
{
    [Fact]
    public void Create_ShouldKeyTheReplicaOnTheUsersServiceUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        string email = Faker.Person.Email;
        string firstName = Faker.Person.FirstName;
        string lastName = Faker.Person.LastName;

        // Act
        var manager = RestaurantManager.Create(userId, email, firstName, lastName);

        // Assert
        manager.Id.Should().Be(userId);
        manager.Email.Should().Be(email);
        manager.FirstName.Should().Be(firstName);
        manager.LastName.Should().Be(lastName);
    }

    [Fact]
    public void Create_ShouldNotRaiseDomainEvents_BecauseTheReplicaProjectsAnotherServicesState()
    {
        // Arrange & Act
        RestaurantManager manager = CreateManager();

        // Assert
        manager.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_ShouldUpdateNames()
    {
        // Arrange
        RestaurantManager manager = CreateManager();
        var firstName = "Updated First";
        var lastName = "Updated Last";

        // Act
        manager.Update(firstName, lastName);

        // Assert
        manager.FirstName.Should().Be(firstName);
        manager.LastName.Should().Be(lastName);
    }

    [Fact]
    public void Update_ShouldNotChangeIdentity()
    {
        // Arrange
        var userId = Guid.NewGuid();
        string email = Faker.Person.Email;
        var manager = RestaurantManager.Create(userId, email, "First", "Last");

        // Act
        manager.Update("Updated First", "Updated Last");

        // Assert
        manager.Id.Should().Be(userId);
        manager.Email.Should().Be(email);
    }

    [Fact]
    public void Update_ShouldNotRaiseDomainEvents_BecauseTheReplicaProjectsAnotherServicesState()
    {
        // Arrange
        RestaurantManager manager = CreateManager();

        // Act
        manager.Update("Updated First", "Updated Last");

        // Assert
        manager.DomainEvents.Should().BeEmpty();
    }

    private static RestaurantManager CreateManager()
    {
        return RestaurantManager.Create(
            Guid.NewGuid(),
            Faker.Person.Email,
            Faker.Person.FirstName,
            Faker.Person.LastName);
    }
}
