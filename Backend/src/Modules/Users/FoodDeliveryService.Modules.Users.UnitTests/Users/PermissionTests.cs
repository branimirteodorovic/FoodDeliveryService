using System.Reflection;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Users.UnitTests.Users;

public class PermissionTests : BaseTest
{
    private static readonly string[] DeclaredCodes = typeof(Permission)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(Permission))
        .Select(field => ((Permission)field.GetValue(null)!).Code)
        .ToArray();

    [Fact]
    public void DeclaredCodes_ShouldBeUnique()
    {
        // Assert
        DeclaredCodes.Should().OnlyHaveUniqueItems();
    }

    // The event-ticketing permissions inherited from the scaffolded project were removed — nothing
    // ever enforced them. Re-introducing one would seed a dead row and a dead grant.
    [Theory]
    [InlineData("events:")]
    [InlineData("ticket-types:")]
    [InlineData("categories:")]
    [InlineData("tickets:")]
    [InlineData("event-statistics:")]
    public void DeclaredCodes_ShouldNotContainEventTicketingPrefix(string prefix)
    {
        // Assert — StartsWith, not Contains: a future "support-tickets:*" code must not trip the
        // bare "tickets:" prefix.
        DeclaredCodes.Should().NotContain(code => code.StartsWith(prefix, StringComparison.Ordinal));
    }
}
