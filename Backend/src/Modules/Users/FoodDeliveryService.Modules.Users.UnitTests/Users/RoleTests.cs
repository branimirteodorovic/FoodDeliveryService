using AwesomeAssertions;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Users.UnitTests.Users;

public class RoleTests : BaseTest
{
    [Theory]
    [InlineData("Customer")]
    [InlineData("RestaurantManager")]
    public void FromName_ShouldReturnRole_WhenNameIsAssignable(string name)
    {
        // Act
        var role = Role.FromName(name);

        // Assert
        role.Should().NotBeNull();
        role.Name.Should().Be(name);
    }

    [Fact]
    public void FromName_ShouldReturnNull_WhenNameIsAdministrator()
    {
        // Administrator is intentionally not assignable — no one can register/be provisioned as admin.

        // Act
        var role = Role.FromName(Role.Administrator.Name);

        // Assert
        role.Should().BeNull();
    }

    [Fact]
    public void FromName_ShouldReturnNull_WhenNameIsUnknown()
    {
        // Act
        var role = Role.FromName("NotARealRole");

        // Assert
        role.Should().BeNull();
    }

    [Fact]
    public void Assignable_ShouldContainCustomerAndRestaurantManagerOnly()
    {
        // Assert
        Role.Assignable.Should().BeEquivalentTo([Role.Customer, Role.RestaurantManager]);
    }
}
