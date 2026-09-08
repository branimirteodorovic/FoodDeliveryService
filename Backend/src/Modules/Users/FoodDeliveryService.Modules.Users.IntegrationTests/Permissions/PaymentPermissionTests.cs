using System.Data.Common;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.Permissions;

/// <summary>
/// Asserts the Feature 3.8 Milestone A seeding end to end, through the same Dapper
/// <c>user_roles → role_permissions</c> join every service resolves over the RPC: a
/// <see cref="Role.Customer"/> resolves <c>payment-methods:manage</c> and <c>payments:read</c> but
/// <b>not</b> the <c>payments:administer</c> ownership bypass, and a <see cref="Role.SupportAgent"/>
/// resolves no payment code at all — an agent who needs to see a payment gets it through the ticket
/// context, never through a direct grant, and <c>refunds:approve</c> is deliberately not widened to
/// also mean "can see payments".
/// <para>
/// Like the other Users integration tests, this calls the real Identity server on :18080 (see
/// <see cref="IntegrationTestWebAppFactory"/>), which must be running.
/// </para>
/// </summary>
public class PaymentPermissionTests : BaseIntegrationTest
{
    public PaymentPermissionTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ProvisionUser_Should_GrantCustomerOwnPaymentCodes_ButNotTheOwnershipBypass()
    {
        // Arrange
        var client = Factory.Services.GetRequiredService<IBus>().CreateRequestClient<ProvisionUserRequest>();

        // Act
        var response = await client.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(UniqueEmail(), Faker.Name.FirstName(), Faker.Name.LastName(), Role.Customer.Name),
            TestContext.Current.CancellationToken);

        // Assert
        response.Is(out Response<ProvisionUserResponse>? success).Should().BeTrue("Customer is an assignable role");

        PermissionsResponse permissions = await ResolvePermissionsAsync(success!.Message.UserId);

        permissions.Permissions.Should().Contain(["payment-methods:manage", "payments:read"]);

        // payments:administer is the administrator's alone. A leak here is silent — the customer
        // would simply start reading other people's payments, with no error anywhere to notice.
        permissions.Permissions.Should().NotContain("payments:administer");
    }

    /// <summary>
    /// Administrator is deliberately not in <see cref="Role.Assignable"/> — no one can be
    /// provisioned as one — so its grants are asserted against the seeded <c>role_permissions</c>
    /// rows directly rather than through a provisioned account.
    /// </summary>
    [Fact]
    public async Task Seeding_Should_GrantAdministratorAllThreePaymentCodes()
    {
        // Arrange
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using DbConnection connection = await connectionFactory.OpenConnectionAsync();

        const string sql =
            """
            SELECT permission_code
            FROM role_permissions
            WHERE role_name = @RoleName AND permission_code LIKE 'payment%'
            """;

        // Act
        IEnumerable<string> codes = await connection.QueryAsync<string>(
            sql, new { RoleName = "Administrator" });

        // Assert
        codes.Should().BeEquivalentTo("payment-methods:manage", "payments:read", "payments:administer");
    }

    [Fact]
    public async Task ProvisionUser_Should_GrantSupportAgentNoPaymentPermissions()
    {
        // Arrange — the agent holds refunds:request and every operational support code. None of
        // that may imply payment access: widening an existing support grant to also mean "can see
        // payments" is the privilege leak the separate `payments:*` namespace exists to prevent.
        var client = Factory.Services.GetRequiredService<IBus>().CreateRequestClient<ProvisionUserRequest>();

        // Act
        var response = await client.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(UniqueEmail(), Faker.Name.FirstName(), Faker.Name.LastName(), Role.SupportAgent.Name),
            TestContext.Current.CancellationToken);

        // Assert
        response.Is(out Response<ProvisionUserResponse>? success).Should().BeTrue("SupportAgent is an assignable role");

        PermissionsResponse permissions = await ResolvePermissionsAsync(success!.Message.UserId);

        permissions.Permissions.Should().NotContain([
            "payment-methods:manage",
            "payments:read",
            "payments:administer"]);
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
