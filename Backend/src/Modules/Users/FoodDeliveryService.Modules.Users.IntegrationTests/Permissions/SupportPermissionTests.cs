using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.Permissions;

/// <summary>
/// Asserts the Feature 3.6 Milestone A seeding end to end, through the same Dapper
/// <c>user_roles → role_permissions</c> join every service uses over the RPC: a provisioned
/// <see cref="Role.SupportAgent"/> resolves the five operational support permissions and crucially
/// <b>not</b> <c>refunds:approve</c> (segregation of duties — the agent who requests a refund must
/// never be able to approve it), and a <see cref="Role.Customer"/> gets only the two customer-facing codes.
/// <para>
/// Like the other Users integration tests, this calls the real Identity server on :18080 (see
/// <see cref="IntegrationTestWebAppFactory"/>), which must be running.
/// </para>
/// </summary>
public class SupportPermissionTests : BaseIntegrationTest
{
    public SupportPermissionTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ProvisionUser_Should_GrantSupportAgentPermissions_ButNotRefundApproval()
    {
        // Arrange
        var client = Factory.Services.GetRequiredService<IBus>().CreateRequestClient<ProvisionUserRequest>();

        // Act
        var response = await client.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(UniqueEmail(), Faker.Name.FirstName(), Faker.Name.LastName(), Role.SupportAgent.Name),
            TestContext.Current.CancellationToken);

        // Assert
        response.Is(out Response<ProvisionUserResponse>? success).Should().BeTrue(
            "SupportAgent is an assignable role");

        PermissionsResponse permissions = await ResolvePermissionsAsync(success!.Message.UserId);

        permissions.Permissions.Should().BeEquivalentTo(
            "support:dashboard",
            "support-tickets:read",
            "support-tickets:manage",
            "support-tickets:assign",
            "refunds:request",
            "support-analytics:read",
            "users:read",
            "users:update");

        // Segregation of duties: approval is the administrator's alone.
        permissions.Permissions.Should().NotContain("refunds:approve");
        // And the customer-facing "open a ticket" code is not an agent's either.
        permissions.Permissions.Should().NotContain("support-tickets:open");
    }

    [Fact]
    public async Task ProvisionUser_Should_GrantCustomerOnlyTheTwoCustomerFacingSupportPermissions()
    {
        // Arrange — provisioning a Customer resolves the same seeded role→permission mapping as
        // self-registration, without depending on the Identity self-registration endpoint.
        var client = Factory.Services.GetRequiredService<IBus>().CreateRequestClient<ProvisionUserRequest>();

        // Act
        var response = await client.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(UniqueEmail(), Faker.Name.FirstName(), Faker.Name.LastName(), Role.Customer.Name),
            TestContext.Current.CancellationToken);

        // Assert
        response.Is(out Response<ProvisionUserResponse>? success).Should().BeTrue("Customer is an assignable role");

        PermissionsResponse permissions = await ResolvePermissionsAsync(success!.Message.UserId);

        permissions.Permissions.Should().Contain(["support-tickets:open", "support-tickets:read"]);
        permissions.Permissions.Should().NotContain([
            "support-tickets:manage",
            "support-tickets:assign",
            "refunds:request",
            "refunds:approve",
            "support-analytics:read",
            "support:dashboard"]);
    }

    private async Task<PermissionsResponse> ResolvePermissionsAsync(Guid userId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        User? user = await userRepository.GetAsync(userId, TestContext.Current.CancellationToken);
        user.Should().NotBeNull();

        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        Result<PermissionsResponse> permissions = await permissionService.GetUserPermissionsAsync(user!.IdentityId);

        permissions.IsSuccess.Should().BeTrue();
        return permissions.Value;
    }
}
