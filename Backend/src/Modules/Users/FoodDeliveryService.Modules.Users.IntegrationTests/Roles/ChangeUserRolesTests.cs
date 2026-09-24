using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Domain.Customers;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Users.Presentation.Users;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.Roles;

/// <summary>
/// <c>PUT users/{userId}/roles</c> end to end: the authorization on it, the aggregate's refusals
/// surfacing as the right status codes, the cross-service permission cache actually being evicted,
/// and a role-filtered replica in another service being back-filled over the bus.
/// <para>
/// The eviction test is the one worth reading. Users' own <c>IPermissionService</c> is uncached
/// (straight Dapper), so nothing in the SUT ever writes <c>user_permissions:{identityId}</c> — the
/// entry under test is written by the in-process <b>Orders</b> host, against the same Redis
/// testcontainer, exactly as one of the seven remote services would in production. That is what
/// makes it a real test of the shared-key property rather than of a local round trip.
/// </para>
/// <para>
/// Like every other test here, this needs the real Identity server on :18080.
/// </para>
/// </summary>
public class ChangeUserRolesTests : BaseIntegrationTest
{
    public ChangeUserRolesTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ChangeUserRoles_Should_ReturnUnauthorized_WhenAnonymous()
    {
        // Act
        HttpResponseMessage response = await HttpClient.PutAsJsonAsync(
            $"users/{Guid.NewGuid()}/roles",
            new ChangeUserRoles.Request { Roles = [Role.SupportAgent.Name] },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangeUserRoles_Should_ReturnForbidden_WhenCallerIsNotAnAdministrator()
    {
        // Arrange — a customer holds users:read and users:update, but never user-roles:manage.
        var email = UniqueEmail();
        Guid userId = await RegisterCustomerAsync(email);
        HttpClient client = await CreateClientForUserAsync(email, StrongPassword);

        // Act — aimed at their own account, so this is the permission refusing and nothing else.
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"users/{userId}/roles",
            new ChangeUserRoles.Request { Roles = [Role.SupportAgent.Name] },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangeUserRoles_Should_ReturnNotFound_WhenTheUserDoesNotExist()
    {
        // Arrange
        HttpClient admin = await CreateAdminClientAsync();

        // Act
        HttpResponseMessage response = await admin.PutAsJsonAsync(
            $"users/{Guid.NewGuid()}/roles",
            new ChangeUserRoles.Request { Roles = [Role.SupportAgent.Name] },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangeUserRoles_Should_ReturnBadRequest_WhenAdministratorIsRequested()
    {
        // Arrange — Administrator is outside Role.Assignable; granting one is not an operation the
        // platform has, and this is the endpoint somebody would try it on.
        Guid userId = await RegisterCustomerAsync(UniqueEmail());
        HttpClient admin = await CreateAdminClientAsync();

        // Act
        HttpResponseMessage response = await admin.PutAsJsonAsync(
            $"users/{userId}/roles",
            new ChangeUserRoles.Request { Roles = [Role.Administrator.Name] },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // …and nothing was applied: the account still resolves the customer permission set.
        PermissionsResponse permissions = await ResolvePermissionsAsync(userId);
        permissions.Permissions.Should().Contain("orders:create");
        permissions.Permissions.Should().NotContain("users:provision");
    }

    [Fact]
    public async Task ChangeUserRoles_Should_ReturnBadRequest_WhenARoleNameIsUnknown()
    {
        // Arrange
        Guid userId = await RegisterCustomerAsync(UniqueEmail());
        HttpClient admin = await CreateAdminClientAsync();

        // Act — one real role and one typo: the whole request must be refused rather than half
        // applied, which is the case a per-name lookup in the repository would silently drop.
        HttpResponseMessage response = await admin.PutAsJsonAsync(
            $"users/{userId}/roles",
            new ChangeUserRoles.Request { Roles = [Role.SupportAgent.Name, "SuportAgent"] },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // support-tickets:MANAGE, not :read — a Customer already holds the read code for their own
        // tickets, so it would pass whether or not the role change had been applied.
        PermissionsResponse permissions = await ResolvePermissionsAsync(userId);
        permissions.Permissions.Should().NotContain("support-tickets:manage");
    }

    [Fact]
    public async Task ChangeUserRoles_Should_ReturnBadRequest_WhenTheRoleSetIsEmpty()
    {
        // Arrange — an account with no roles resolves no permissions, which every service reports as
        // an unknown user. The aggregate refuses it.
        Guid userId = await RegisterCustomerAsync(UniqueEmail());
        HttpClient admin = await CreateAdminClientAsync();

        // Act
        HttpResponseMessage response = await admin.PutAsJsonAsync(
            $"users/{userId}/roles",
            new ChangeUserRoles.Request { Roles = [] },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeUserRoles_Should_ReturnConflict_WhenTheTargetIsAnAdministrator()
    {
        // Arrange — the replacement set can only hold assignable roles, so applying one to an
        // administrator would strip the role. The aggregate refuses rather than doing that quietly.
        HttpClient admin = await CreateAdminClientAsync();

        CurrentUser adminUser = await GetCurrentUserAsync(admin);

        // Act
        HttpResponseMessage response = await admin.PutAsJsonAsync(
            $"users/{adminUser.UserId}/roles",
            new ChangeUserRoles.Request { Roles = [Role.Customer.Name] },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The administrator still is one — otherwise this fixture's remaining tests would have no
        // caller, which is exactly the lockout the guard exists to prevent.
        CurrentUser stillAdmin = await GetCurrentUserAsync(admin);
        stillAdmin.Roles.Should().Contain(Role.Administrator.Name);
    }

    [Fact]
    public async Task ChangeUserRoles_Should_ReplaceTheRoleSet_AndBeVisibleOnTheUsersOwnProfile()
    {
        // Arrange
        var email = UniqueEmail();
        Guid userId = await RegisterCustomerAsync(email);
        HttpClient admin = await CreateAdminClientAsync();

        // Act
        HttpResponseMessage response = await admin.PutAsJsonAsync(
            $"users/{userId}/roles",
            new ChangeUserRoles.Request { Roles = [Role.SupportAgent.Name] },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A replacement, not a grant: Customer is gone. Read through the user's own token, which is
        // also the proof that the change needs no reissued token — it carries no roles to go stale.
        HttpClient user = await CreateClientForUserAsync(email, StrongPassword);
        CurrentUser profile = await GetCurrentUserAsync(user);

        profile.Roles.Should().BeEquivalentTo(Role.SupportAgent.Name);
    }

    [Fact]
    public async Task ChangeUserRoles_Should_EvictTheSharedPermissionCacheEntry_ForEveryService()
    {
        // Arrange — register a customer, then warm the permission cache the way a remote service
        // does: through the in-process Orders host's IPermissionService, which RPCs the Users SUT
        // over the shared broker and caches the answer in the shared Redis for five minutes.
        Guid userId = await RegisterCustomerAsync(UniqueEmail());
        string identityId = await GetIdentityIdAsync(userId);

        PermissionsResponse warmed = await ResolvePermissionsThroughOrdersAsync(identityId);
        warmed.Permissions.Should().Contain("orders:create", "the customer set is what is now cached");
        warmed.Permissions.Should().NotContain("support-tickets:manage");

        string cacheKey = CacheKeys.UserPermissions(identityId);

        PermissionsResponse? cached = await ReadCacheEntryAsync(cacheKey);
        cached.Should().NotBeNull("the warm-up must actually have cached something under this key");

        HttpClient admin = await CreateAdminClientAsync();

        // Act
        HttpResponseMessage response = await admin.PutAsJsonAsync(
            $"users/{userId}/roles",
            new ChangeUserRoles.Request { Roles = [Role.SupportAgent.Name] },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert — the entry is gone, immediately and with no polling. The command evicted it inline
        // after SaveChangesAsync; nothing waited for an outbox tick. Asserted before re-resolving,
        // because resolving repopulates it.
        PermissionsResponse? evicted = await ReadCacheEntryAsync(cacheKey);
        evicted.Should().BeNull(
            "the eviction in the Users command handler must reach the entry the Orders host wrote — " +
            "there is one entry per user for the whole platform, with no service qualifier");

        // …and the next authorization in that other service sees the new roles, not the five-minute
        // stale ones.
        PermissionsResponse fresh = await ResolvePermissionsThroughOrdersAsync(identityId);
        fresh.Permissions.Should().Contain("support-tickets:manage");
        fresh.Permissions.Should().NotContain("orders:create");
    }

    [Fact]
    public async Task ChangeUserRoles_Should_BackfillTheOrdersCustomerReplica_WhenCustomerIsGranted()
    {
        // Arrange — a provisioned driver, who has never been a Customer and therefore has no row in
        // the Orders replica that PlaceOrder requires.
        IRequestClient<ProvisionUserRequest> provisionClient =
            Factory.Services.GetRequiredService<IBus>().CreateRequestClient<ProvisionUserRequest>();

        var provisionResponse = await provisionClient.GetResponse<ProvisionUserResponse, Error>(
            new ProvisionUserRequest(
                UniqueEmail(),
                Faker.Name.FirstName(),
                Faker.Name.LastName(),
                Role.DeliveryDriver.Name),
            TestContext.Current.CancellationToken);

        provisionResponse.Is(out Response<ProvisionUserResponse>? provisioned).Should().BeTrue();
        Guid userId = provisioned!.Message.UserId;

        await using (AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope())
        {
            var customerRepository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
            Customer? absent = await customerRepository.GetAsync(userId, TestContext.Current.CancellationToken);

            absent.Should().BeNull("the Orders replica is role-filtered and a driver is not a customer");
        }

        HttpClient admin = await CreateAdminClientAsync();

        // Act — the driver is now also allowed to order.
        HttpResponseMessage changed = await admin.PutAsJsonAsync(
            $"users/{userId}/roles",
            new ChangeUserRoles.Request { Roles = [Role.DeliveryDriver.Name, Role.Customer.Name] },
            TestContext.Current.CancellationToken);

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert — the replica arrives asynchronously: Users outbox (≤1s) publishes
        // UserRolesChangedIntegrationEvent → RabbitMQ → Orders inbox (≤1s) dispatches
        // UpsertCustomerCommand. Without this event the grant would hand them orders:create and a
        // 404 on their first order.
        Result<Customer> replicaResult = await Poller.WaitAsync<Customer>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope();

                var customerRepository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();

                return await customerRepository.GetAsync(userId, TestContext.Current.CancellationToken);
            });

        replicaResult.IsSuccess.Should().BeTrue(
            "granting the Customer role must back-fill the replica PlaceOrder requires");
        replicaResult.Value.Id.Should().Be(userId);
    }

    private static async Task<CurrentUser> GetCurrentUserAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync(
            new Uri("users/me", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CurrentUser>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>The module-side user's identity-provider id — what the cache key is built from.</summary>
    private async Task<string> GetIdentityIdAsync(Guid userId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        User? user = await userRepository.GetAsync(userId, TestContext.Current.CancellationToken);

        user.Should().NotBeNull();

        return user!.IdentityId;
    }

    /// <summary>Resolves permissions inside the SUT — uncached, so it never touches the entry under test.</summary>
    private async Task<PermissionsResponse> ResolvePermissionsAsync(Guid userId)
    {
        string identityId = await GetIdentityIdAsync(userId);

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        Result<PermissionsResponse> result = await permissionService.GetUserPermissionsAsync(identityId);

        result.IsSuccess.Should().BeTrue();

        return result.Value;
    }

    /// <summary>
    /// Resolves permissions the way one of the seven remote services does: Orders' own
    /// <c>PermissionService</c>, an RPC to Users behind the five-minute Redis cache. Calling this
    /// both warms the entry and proves what a later request would be authorized against.
    /// </summary>
    private async Task<PermissionsResponse> ResolvePermissionsThroughOrdersAsync(string identityId)
    {
        await using AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope();

        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        Result<PermissionsResponse> result = await permissionService.GetUserPermissionsAsync(identityId);

        result.IsSuccess.Should().BeTrue("the permissions RPC to the Users SUT must answer");

        return result.Value;
    }

    /// <summary>
    /// Reads the raw cache entry. Deliberately through the <b>Orders</b> host's cache service: it is
    /// the one that wrote the entry, so a key that only matched by coincidence would show up here as
    /// a miss rather than passing silently.
    /// </summary>
    private async Task<PermissionsResponse?> ReadCacheEntryAsync(string key)
    {
        await using AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope();

        var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();

        return await cacheService.GetAsync<PermissionsResponse>(key, TestContext.Current.CancellationToken);
    }

    /// <summary>The shape of <c>GET users/me</c> — only the two fields these tests read.</summary>
    private sealed record CurrentUser(Guid UserId, IReadOnlyCollection<string> Roles);
}
