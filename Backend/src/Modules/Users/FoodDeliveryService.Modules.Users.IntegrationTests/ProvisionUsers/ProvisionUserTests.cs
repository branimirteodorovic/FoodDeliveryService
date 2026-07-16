using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.ProvisionUsers;

/// <summary>
/// Exercises the generalized <see cref="ProvisionUserRequest"/> RPC (Feature 2.1 Milestone A) end to
/// end over the real bus against the Users SUT: a valid assignable role provisions an invited account
/// and grants that role's permission set; an unknown or non-assignable role (Administrator) and a
/// duplicate email are refused as clean Error responses, never a 500/timeout.
/// <para>
/// Like the register tests, invited-account provisioning calls the real Identity server on
/// :18080 (see <see cref="IntegrationTestWebAppFactory"/>), which must be running.
/// </para>
/// </summary>
public class ProvisionUserTests : BaseIntegrationTest
{
    public ProvisionUserTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ProvisionUser_Should_ReturnUserId_AndGrantDeliveryDriverPermissions_WhenRoleIsDeliveryDriver()
    {
        // Arrange
        var email = UniqueEmail();
        IRequestClient<ProvisionUserRequest> client = CreateProvisionClient();

        // Act
        var response = await client.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(email, Faker.Name.FirstName(), Faker.Name.LastName(), Role.DeliveryDriver.Name),
            TestContext.Current.CancellationToken);

        // Assert — the RPC succeeded and carries the new module UserId.
        response.Is(out Response<ProvisionUserResponse>? success).Should().BeTrue(
            "provisioning an assignable role must succeed");
        Guid userId = success!.Message.UserId;
        userId.Should().NotBeEmpty();

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        // The module-side account exists…
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        User? user = await userRepository.GetAsync(userId, TestContext.Current.CancellationToken);
        user.Should().NotBeNull();
        user!.Email.Should().Be(email);

        // …and resolves exactly the DeliveryDriver permission set (proving the role was assigned and
        // mapped) — crucially NOT deliveries:administer, the admin-only ownership bypass. The Dapper
        // query joins user_roles → role_permissions, so this also exercises the seeded role mapping.
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        Result<PermissionsResponse> permissions =
            await permissionService.GetUserPermissionsAsync(user.IdentityId);

        permissions.IsSuccess.Should().BeTrue();
        permissions.Value.Permissions.Should().BeEquivalentTo(
            "drivers:read",
            "drivers:update",
            "deliveries:read",
            "deliveries:manage",
            "users:read",
            "users:update");
        permissions.Value.Permissions.Should().NotContain("deliveries:administer");
    }

    [Fact]
    public async Task ProvisionUser_Should_ReturnErrorResponse_WhenRoleIsAdministrator()
    {
        // Arrange — Administrator is intentionally non-assignable; the consumer must refuse it cleanly
        // (an Error response) rather than let it surface as a 500/timeout.
        IRequestClient<ProvisionUserRequest> client = CreateProvisionClient();

        // Act
        var response = await client.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(UniqueEmail(), Faker.Name.FirstName(), Faker.Name.LastName(), Role.Administrator.Name),
            TestContext.Current.CancellationToken);

        // Assert
        response.Is(out Response<Error>? failure).Should().BeTrue("a non-assignable role must fail cleanly");
        failure!.Message.Code.Should().Be("Users.RoleNotAssignable");
    }

    [Fact]
    public async Task ProvisionUser_Should_ReturnErrorResponse_WhenRoleIsUnknown()
    {
        // Arrange
        IRequestClient<ProvisionUserRequest> client = CreateProvisionClient();

        // Act
        var response = await client.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(UniqueEmail(), Faker.Name.FirstName(), Faker.Name.LastName(), "NotARealRole"),
            TestContext.Current.CancellationToken);

        // Assert
        response.Is(out Response<Error>? failure).Should().BeTrue("an unknown role must fail cleanly");
        failure!.Message.Code.Should().Be("Users.RoleNotAssignable");
    }

    [Fact]
    public async Task ProvisionUser_Should_ReturnErrorResponse_WhenEmailIsDuplicate()
    {
        // Arrange — provision once, then re-provision the same email. Identity rejects the duplicate,
        // and the consumer relays that as an Error response, not an exception.
        var email = UniqueEmail();
        IRequestClient<ProvisionUserRequest> client = CreateProvisionClient();

        var first = await client.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(email, Faker.Name.FirstName(), Faker.Name.LastName(), Role.DeliveryDriver.Name),
            TestContext.Current.CancellationToken);
        first.Is(out Response<ProvisionUserResponse>? _).Should().BeTrue("the first provision should succeed");

        // Act
        var response = await client.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(email, Faker.Name.FirstName(), Faker.Name.LastName(), Role.DeliveryDriver.Name),
            TestContext.Current.CancellationToken);

        // Assert
        response.Is(out Response<Error>? failure).Should().BeTrue("a duplicate email must fail cleanly");
        failure!.Message.Should().NotBeNull();
    }

    private IRequestClient<ProvisionUserRequest> CreateProvisionClient() =>
        Factory.Services.GetRequiredService<IBus>().CreateRequestClient<ProvisionUserRequest>();
}
