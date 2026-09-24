using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Modules.Users.Application.Users.GetCurrentUser;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.CurrentUser;

/// <summary>
/// <c>GET users/me</c> — the read a client makes once after sign-in to find out who it is signed in
/// as. Everything it returns is something the access token does not carry, so these tests drive the
/// real Duende instance on <c>:18080</c> for a real password-grant token rather than a fabricated
/// principal: a stubbed <c>sub</c> would make the very mismatch under test impossible to observe.
/// <para>
/// That instance must be running. Note that KinD and docker-compose both bind host <c>:18080</c>,
/// so only one of them may be up while these run.
/// </para>
/// </summary>
public class GetCurrentUserTests : BaseIntegrationTest
{
    public GetCurrentUserTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetCurrentUser_Should_ReturnTheCallersOwnRecord_WithTheirRoles()
    {
        // Arrange — a real self-registered customer, activated by the act of registering.
        string email = UniqueEmail();
        await RegisterCustomerAsync(email);

        using HttpClient client = await CreateClientForUserAsync(email, StrongPassword);

        // Act
        HttpResponseMessage response = await client.GetAsync("users/me", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        CurrentUserResponse? me = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(
            TestContext.Current.CancellationToken);

        me.Should().NotBeNull();
        me!.Email.Should().Be(email);
        me.FirstName.Should().NotBeNullOrWhiteSpace();
        me.LastName.Should().NotBeNullOrWhiteSpace();

        // The roles are the point of the endpoint: they live only in user_roles, and the API
        // resource declares no UserClaims, so nothing on the client can learn them any other way.
        // Exactly one, and exactly Customer — self-registration forces the role regardless of what
        // was sent, so anything else here is a privilege escalation rather than a failed mapping.
        me.Roles.Should().BeEquivalentTo(Role.Customer.Name);
    }

    [Fact]
    public async Task GetCurrentUser_Should_ReturnTheModuleSideUserId_NotTheIdentityId()
    {
        // Arrange — registration answers with the module-side id (users.id). The token's sub is the
        // Duende identity id. They are two different values for the same person, and confusing them
        // is the specific bug this endpoint exists to make impossible: the wrong one matches no
        // OrderResponse.customerId and no SignalR user:{id} group, and fails silently by returning
        // an empty list rather than an error.
        string email = UniqueEmail();
        Guid registeredUserId = await RegisterCustomerAsync(email);

        string accessToken = await GetAccessTokenAsync(email, StrongPassword);
        string subject = SubjectClaim(accessToken);

        using HttpClient client = await CreateClientForUserAsync(email, StrongPassword);

        // Act
        CurrentUserResponse? me = await client.GetFromJsonAsync<CurrentUserResponse>(
            "users/me",
            TestContext.Current.CancellationToken);

        // Assert
        me.Should().NotBeNull();
        me!.UserId.Should().Be(registeredUserId, "users/me must report users.id — the id every other service keys this customer by");

        // Belt and braces, from both ends: the token's subject is the identity id, and the row says
        // the same. If a future change ever made the two ids equal, this test would start passing
        // for the wrong reason without the row check.
        me.UserId.ToString().Should().NotBe(subject, "the token's sub is the identity-provider id");
        subject.Should().Be(await IdentityIdAsync(registeredUserId));
    }

    [Fact]
    public async Task GetCurrentUser_Should_Return401_ForAnAnonymousCaller()
    {
        // Arrange — the factory's plain client carries no token. users/me is the first authenticated
        // endpoint in this module; its two neighbours (register, accept-invitation) are anonymous by
        // design, so "the Users host rejects an unauthenticated caller" has never been asserted here.
        using HttpClient anonymous = Factory.CreateClient();

        // Act
        HttpResponseMessage response = await anonymous.GetAsync("users/me", TestContext.Current.CancellationToken);

        // Assert — 401, not 403: there is no principal at all, so nothing was denied a permission.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The <c>sub</c> claim out of the raw JWT payload, decoded by hand. No token handler is pulled
    /// in for it: the signature was already validated by the host that accepted the call, and what
    /// is needed here is one string out of the middle segment.
    /// </summary>
    private static string SubjectClaim(string accessToken)
    {
        string payload = accessToken.Split('.')[1];

        // base64url → base64, then pad to a multiple of four.
        payload = payload.Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

        return document.RootElement.GetProperty("sub").GetString()!;
    }

    private async Task<string> IdentityIdAsync(Guid userId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using DbConnection connection = await connectionFactory.OpenConnectionAsync();

        const string sql = "SELECT identity_id FROM users WHERE id = @UserId";

        return await connection.QuerySingleAsync<string>(sql, new { UserId = userId });
    }
}
