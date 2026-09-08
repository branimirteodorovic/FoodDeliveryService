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

    /// <summary>
    /// Feature 3.8. The payment set must be its own namespace, and in particular
    /// <c>payments:administer</c> must exist as a distinct code rather than being inferred from an
    /// unrelated administrator grant such as <c>refunds:approve</c> — a privilege leaking through
    /// an unrelated permission is exactly the trap the `support-*` namespace was carved out to avoid.
    /// </summary>
    [Fact]
    public void PaymentCodes_ShouldBeTheirOwnNamespace()
    {
        // Assert
        Permission.ManagePaymentMethods.Code.Should().Be("payment-methods:manage");
        Permission.GetPayments.Code.Should().Be("payments:read");
        Permission.AdministerPayments.Code.Should().Be("payments:administer");

        // The ownership bypass is a code of its own, not a re-reading of a support permission.
        Permission.AdministerPayments.Code.Should().NotBe(Permission.ApproveRefund.Code);
        All.Where(p => p.Code.StartsWith("payments:", StringComparison.Ordinal))
            .Should().HaveCount(2, "only payments:read and payments:administer live in that namespace");
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
