using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Users.UnitTests.Users;

/// <summary>
/// <see cref="User.ChangeRoles"/> — the only role mutation the aggregate has, and the trigger the
/// cross-service permission cache is invalidated for.
/// </summary>
public class ChangeUserRolesTests : BaseTest
{
    [Fact]
    public void ChangeRoles_ShouldReplaceTheSet_WhenRolesDiffer()
    {
        // Arrange — a self-registered customer, as every account that is not invited starts.
        User user = CreateUser();

        // Act
        Result result = user.ChangeRoles([Role.SupportAgent]);

        // Assert — replaced, not added to: the previous role is gone.
        result.IsSuccess.Should().BeTrue();
        user.Roles.Select(role => role.Name).Should().BeEquivalentTo(Role.SupportAgent.Name);
    }

    [Fact]
    public void ChangeRoles_ShouldRaiseDomainEventCarryingTheResultingSet_WhenRolesChange()
    {
        // Arrange
        User user = CreateUser();

        // Act
        Result result = user.ChangeRoles([Role.Customer, Role.DeliveryDriver]);

        // Assert — the event carries the whole set, not the delta: consumers replicate snapshots.
        result.IsSuccess.Should().BeTrue();
        UserRolesChangedDomainEvent domainEvent = AssertDomainEventWasPublished<UserRolesChangedDomainEvent>(user);
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.Roles.Should().BeEquivalentTo(Role.Customer.Name, Role.DeliveryDriver.Name);
    }

    [Fact]
    public void ChangeRoles_ShouldNotRaiseDomainEvent_WhenTheSetIsUnchanged()
    {
        // Arrange — the same role the user already holds.
        User user = CreateUser();

        // Act
        Result result = user.ChangeRoles([Role.Customer]);

        // Assert — a no-op succeeds silently, exactly as Update does: no event, so no outbox row and
        // nothing downstream told a change happened that did not.
        result.IsSuccess.Should().BeTrue();
        user.Roles.Select(role => role.Name).Should().BeEquivalentTo(Role.Customer.Name);
        user.DomainEvents.OfType<UserRolesChangedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void ChangeRoles_ShouldNotRaiseDomainEvent_WhenTheSameSetIsSentInADifferentOrder()
    {
        // Arrange — order is not part of the set's identity, so this must be a no-op too.
        User user = CreateUser();
        user.ChangeRoles([Role.Customer, Role.SupportAgent]);
        user.ClearDomainEvents();

        // Act
        Result result = user.ChangeRoles([Role.SupportAgent, Role.Customer]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.DomainEvents.OfType<UserRolesChangedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void ChangeRoles_ShouldTreatDuplicatesAsOneRole_WhenTheSameNameIsSentTwice()
    {
        // Arrange — a caller repeating a role must not produce two user_roles rows, and must not
        // make an otherwise-unchanged set look changed by its count.
        User user = CreateUser();

        // Act
        Result result = user.ChangeRoles([Role.Customer, Role.Customer]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.Roles.Should().ContainSingle();
        user.DomainEvents.OfType<UserRolesChangedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void ChangeRoles_ShouldFail_WhenTheSetIsEmpty()
    {
        // Arrange — an account with no roles resolves no permissions at all, which every service
        // reports as an unknown user rather than as a restricted one.
        User user = CreateUser();

        // Act
        Result result = user.ChangeRoles([]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(UserErrors.AtLeastOneRoleRequired);
        user.Roles.Select(role => role.Name).Should().BeEquivalentTo(Role.Customer.Name);
        user.DomainEvents.OfType<UserRolesChangedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void ChangeRoles_ShouldFail_WhenAdministratorIsRequested()
    {
        // Arrange — Administrator is deliberately outside Role.Assignable; the seed admin comes from
        // configuration and nothing on the platform may mint another.
        User user = CreateUser();

        // Act
        Result result = user.ChangeRoles([Role.Administrator]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(UserErrors.RoleNotAssignable(Role.Administrator.Name).Code);
        user.Roles.Select(role => role.Name).Should().BeEquivalentTo(Role.Customer.Name);
    }

    [Fact]
    public void ChangeRoles_ShouldFail_WhenOneOfSeveralRolesIsNotAssignable()
    {
        // Arrange — the whole request is refused; a partially applied role set is worse than none.
        User user = CreateUser();

        // Act
        Result result = user.ChangeRoles([Role.SupportAgent, Role.Administrator]);

        // Assert
        result.IsFailure.Should().BeTrue();
        user.Roles.Select(role => role.Name).Should().BeEquivalentTo(Role.Customer.Name);
        user.DomainEvents.OfType<UserRolesChangedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void ChangeRoles_ShouldFail_WhenTheUserIsAnAdministrator()
    {
        // Arrange — the replacement set can only hold assignable roles, and Administrator is not one,
        // so any change applied here would silently strip it: possibly from the last administrator.
        var user = User.Create(
            Faker.Person.Email,
            Faker.Person.FirstName,
            Faker.Person.LastName,
            Guid.NewGuid().ToString(),
            Role.Administrator);

        // Act
        Result result = user.ChangeRoles([Role.Customer]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(UserErrors.AdministratorRolesAreImmutable);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        user.Roles.Select(role => role.Name).Should().BeEquivalentTo(Role.Administrator.Name);
    }

    [Fact]
    public void ChangeRoles_ShouldCompareByName_WhenRolesAreDistinctInstances()
    {
        // Arrange — Role has no equality override, and EF materializes a fresh instance per row, so
        // the roles a handler passes in are never the same objects as the Role statics. A comparison
        // by reference would report every set as changed and re-publish on every call.
        User user = CreateUser();
        user.ChangeRoles([Role.SupportAgent]);
        user.ClearDomainEvents();

        Role sameRoleDifferentInstance = Role.FromName(Role.SupportAgent.Name)!;

        // Act
        Result result = user.ChangeRoles([sameRoleDifferentInstance]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.DomainEvents.OfType<UserRolesChangedDomainEvent>().Should().BeEmpty();
    }

    private static User CreateUser() =>
        User.Create(
            Faker.Person.Email,
            Faker.Person.FirstName,
            Faker.Person.LastName,
            Guid.NewGuid().ToString());
}
