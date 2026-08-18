using System.Reflection;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Users.UnitTests.Users;

/// <summary>
/// Guards the permission catalogue. The original event-ticketing scaffold's codes
/// (<c>events:*</c>, <c>ticket-types:*</c>, <c>categories:*</c>, <c>tickets:*</c>,
/// <c>event-statistics:read</c>) were removed — this platform delivers food, and
/// <c>tickets:read</c> in particular was granted to every Customer, so reviving it would silently
/// hand support access to the entire customer base.
/// </summary>
public class PermissionTests : BaseTest
{
    private static readonly IReadOnlyList<Permission> All = typeof(Permission)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(f => f.FieldType == typeof(Permission))
        .Select(f => (Permission)f.GetValue(null)!)
        .ToList();

    [Fact]
    public void Codes_ShouldBeUnique()
    {
        // Assert
        All.Select(p => p.Code).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("events:")]
    [InlineData("ticket-types:")]
    [InlineData("categories:")]
    [InlineData("tickets:")]
    [InlineData("event-statistics:")]
    public void Codes_ShouldNotReviveEventTicketingNamespaces(string prefix)
    {
        // Assert — note `support-tickets:` must not trip the bare `tickets:` case.
        All.Should().AllSatisfy(p =>
            p.Code.StartsWith(prefix, StringComparison.Ordinal).Should().BeFalse(
                $"{p.Code} belongs to the removed event-ticketing scaffold"));
    }

    [Fact]
    public void SupportTicketCodes_ShouldUseTheSupportTicketsPrefix()
    {
        // Assert
        new[]
        {
            Permission.OpenSupportTicket,
            Permission.GetSupportTickets,
            Permission.ManageSupportTickets,
            Permission.AssignSupportTickets
        }.Should().AllSatisfy(p => p.Code.Should().StartWith("support-tickets:"));
    }
}
