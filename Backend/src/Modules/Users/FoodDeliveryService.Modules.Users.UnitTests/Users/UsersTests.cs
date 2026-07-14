using AwesomeAssertions;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Users.UnitTests.Users;

public class UsersTests : BaseTest
{
    [Fact]
    public void Create_ShouldSetProperties_WhenUserIsCreated()
    {
        // Arrange
        var email = Faker.Person.Email;
        var firstName = Faker.Person.FirstName;
        var lastName = Faker.Person.LastName;
        var identityId = Guid.NewGuid().ToString();

        // Act
        var user = User.Create(email, firstName, lastName, identityId);

        // Assert
        user.Id.Should().NotBe(Guid.Empty);
        user.Email.Should().Be(email);
        user.FirstName.Should().Be(firstName);
        user.LastName.Should().Be(lastName);
        user.IdentityId.Should().Be(identityId);
    }

    [Fact]
    public void Create_ShouldAssignCustomerRole_WhenNoRoleIsSpecified()
    {
        // Arrange & Act
        User user = CreateUser();

        // Assert
        user.Roles.Should().ContainSingle().Which.Should().Be(Role.Customer);
    }

    [Fact]
    public void Create_ShouldRaiseUserRegisteredDomainEvent_WhenUserIsCreated()
    {
        // Arrange & Act
        User user = CreateUser();

        // Assert
        UserRegisteredDomainEvent domainEvent = AssertDomainEventWasPublished<UserRegisteredDomainEvent>(user);
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.Roles.Should().ContainSingle().Which.Should().Be(Role.Customer.Name);
    }

    [Fact]
    public void Create_ShouldAssignSpecifiedRole_WhenRoleIsProvided()
    {
        // Arrange
        var email = Faker.Person.Email;
        var firstName = Faker.Person.FirstName;
        var lastName = Faker.Person.LastName;
        var identityId = Guid.NewGuid().ToString();

        // Act
        var user = User.Create(email, firstName, lastName, identityId, Role.RestaurantManager);

        // Assert
        user.Roles.Should().ContainSingle().Which.Should().Be(Role.RestaurantManager);
        UserRegisteredDomainEvent domainEvent = AssertDomainEventWasPublished<UserRegisteredDomainEvent>(user);
        domainEvent.Roles.Should().ContainSingle().Which.Should().Be(Role.RestaurantManager.Name);
    }

    [Fact]
    public void CreateInvited_ShouldSetPropertiesAndAssignRole_WhenInvited()
    {
        // Arrange
        var email = Faker.Person.Email;
        var firstName = Faker.Person.FirstName;
        var lastName = Faker.Person.LastName;
        var identityId = Guid.NewGuid().ToString();

        // Act
        var user = User.CreateInvited(
            email,
            firstName,
            lastName,
            identityId,
            Role.RestaurantManager,
            Faker.Random.AlphaNumeric(32),
            DateTime.UtcNow.AddHours(1));

        // Assert
        user.Email.Should().Be(email);
        user.FirstName.Should().Be(firstName);
        user.LastName.Should().Be(lastName);
        user.IdentityId.Should().Be(identityId);
        user.Roles.Should().ContainSingle().Which.Should().Be(Role.RestaurantManager);
    }

    [Fact]
    public void CreateInvited_ShouldRaiseUserRegisteredDomainEvent_WhenInvited()
    {
        // Arrange & Act
        User user = CreateInvitedUser();

        // Assert
        UserRegisteredDomainEvent domainEvent = AssertDomainEventWasPublished<UserRegisteredDomainEvent>(user);
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.Roles.Should().ContainSingle().Which.Should().Be(Role.RestaurantManager.Name);
    }

    [Fact]
    public void CreateInvited_ShouldRaiseUserInvitedDomainEvent_CarryingActivationToken()
    {
        // Arrange
        var email = Faker.Person.Email;
        var firstName = Faker.Person.FirstName;
        var lastName = Faker.Person.LastName;
        var activationToken = Faker.Random.AlphaNumeric(32);
        var expiresOnUtc = DateTime.UtcNow.AddHours(1);

        // Act
        var user = User.CreateInvited(
            email,
            firstName,
            lastName,
            Guid.NewGuid().ToString(),
            Role.RestaurantManager,
            activationToken,
            expiresOnUtc);

        // Assert
        UserInvitedDomainEvent domainEvent = AssertDomainEventWasPublished<UserInvitedDomainEvent>(user);
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.Email.Should().Be(email);
        domainEvent.FirstName.Should().Be(firstName);
        domainEvent.LastName.Should().Be(lastName);
        domainEvent.ActivationToken.Should().Be(activationToken);
        domainEvent.ExpiresOnUtc.Should().Be(expiresOnUtc);
    }

    [Fact]
    public void Update_ShouldChangeNamesAndRaiseDomainEvent_WhenNamesChanged()
    {
        // Arrange
        User user = CreateUser();
        var newFirstName = Faker.Name.FirstName();
        var newLastName = Faker.Name.LastName();

        // Act
        user.Update(newFirstName, newLastName);

        // Assert
        user.FirstName.Should().Be(newFirstName);
        user.LastName.Should().Be(newLastName);
        UserProfileUpdatedDomainEvent domainEvent = AssertDomainEventWasPublished<UserProfileUpdatedDomainEvent>(user);
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.FirstName.Should().Be(newFirstName);
        domainEvent.LastName.Should().Be(newLastName);
    }

    [Fact]
    public void Update_ShouldRaiseDomainEvent_WhenOnlyFirstNameChanges()
    {
        // Arrange
        User user = CreateUser(out _, out string firstName, out string lastName, out _);
        var newFirstName = firstName + "Changed";

        // Act
        user.Update(newFirstName, lastName);

        // Assert
        user.FirstName.Should().Be(newFirstName);
        user.LastName.Should().Be(lastName);
        AssertDomainEventWasPublished<UserProfileUpdatedDomainEvent>(user);
    }

    [Fact]
    public void Update_ShouldNotRaiseDomainEvent_WhenNamesAreUnchanged()
    {
        // Arrange
        User user = CreateUser(out _, out string firstName, out string lastName, out _);

        // Act
        user.Update(firstName, lastName);

        // Assert
        user.DomainEvents.OfType<UserProfileUpdatedDomainEvent>().Should().BeEmpty();
    }

    private static User CreateUser()
    {
        return CreateUser(out _, out _, out _, out _);
    }

    private static User CreateUser(
        out string email,
        out string firstName,
        out string lastName,
        out string identityId)
    {
        email = Faker.Person.Email;
        firstName = Faker.Person.FirstName;
        lastName = Faker.Person.LastName;
        identityId = Guid.NewGuid().ToString();

        return User.Create(email, firstName, lastName, identityId);
    }

    private static User CreateInvitedUser()
    {
        return User.CreateInvited(
            Faker.Person.Email,
            Faker.Person.FirstName,
            Faker.Person.LastName,
            Guid.NewGuid().ToString(),
            Role.RestaurantManager,
            Faker.Random.AlphaNumeric(32),
            DateTime.UtcNow.AddHours(1));
    }
}
