using System.Reflection;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Users.UnitTests.Users;

/// <summary>
/// Guards the Feature 3.6 naming decision. The pre-existing <c>tickets:read</c> /
/// <c>tickets:check-in</c> codes are event-ticketing leftovers from the Evently heritage and are
/// granted to every Customer; reusing them for support ticketing would silently hand support access
/// to the entire customer base. These tests are cheap and catch exactly that copy-paste regression.
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

    [Fact]
    public void SupportCodes_ShouldNotCollideWithEventTicketingCodes()
    {
        // Arrange — every code the support feature introduces.
        Permission[] supportPermissions =
        [
            Permission.OpenSupportTicket,
            Permission.GetSupportTickets,
            Permission.ManageSupportTickets,
            Permission.AssignSupportTickets,
            Permission.RequestRefund,
            Permission.ApproveRefund,
            Permission.GetSupportAnalytics
        ];

        // Assert — none of them is one of the customer-granted event-ticketing codes…
        string[] eventTicketingCodes = [Permission.GetTickets.Code, Permission.CheckInTicket.Code];
        supportPermissions.Select(p => p.Code).Should().NotIntersectWith(eventTicketingCodes);

        // …and none of them sits in the bare `tickets:` namespace at all.
        supportPermissions.Should().AllSatisfy(p =>
            p.Code.StartsWith("tickets:", StringComparison.Ordinal).Should().BeFalse(
                $"{p.Code} must not live in the event-ticketing namespace"));
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
