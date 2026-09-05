using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Users.Presentation.Users;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.Lockout;

/// <summary>
/// Feature 3.7 Milestone E §6.4. Drives the real Duende token endpoint on <c>:18080</c> — the one
/// place in this platform where a password is checked, and the one endpoint that does not pass
/// through the Gateway, so the edge rate limiter never sees it.
/// <para>
/// The assertion is deliberately black-box and indirect. Duende answers <c>invalid_grant</c> for a
/// wrong password and <c>invalid_grant</c> for a locked-out account — identical bodies, which is
/// correct behaviour (the response must not tell an attacker which of the two happened) and means
/// lockout cannot be observed from a failure. What proves it is the <b>correct</b> password failing
/// afterwards.
/// </para>
/// <para>
/// These tests need <c>fooddeliveryservice.identity</c> running on <c>:18080</c> with the Milestone E
/// build — an Identity image from before it has no lockout configured and the last step here passes
/// where it should fail.
/// </para>
/// </summary>
public class AccountLockoutTests : BaseIntegrationTest
{
    private const string TokenEndpoint = "http://localhost:18080/connect/token";
    private const string PublicClientId = "fooddeliveryservice-public-client";

    /// <summary>
    /// Mirrors <c>options.Lockout.MaxFailedAccessAttempts</c> in the Identity host. One more failure
    /// than this is what trips the lock.
    /// </summary>
    private const int MaxFailedAccessAttempts = 5;

    public AccountLockoutTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task TokenEndpoint_Should_LockTheAccount_AfterTheConfiguredNumberOfFailedAttempts()
    {
        // Arrange — a fresh account through the real registration path, so the credential exists in
        // Identity exactly as a customer's would.
        string email = UniqueEmail();
        await RegisterAsync(email);

        // The account works before anything is attempted against it. Without this the test could
        // pass for the wrong reason — an account that never worked also fails at the end.
        (await RequestTokenAsync(email, StrongPassword)).StatusCode
            .Should().Be(HttpStatusCode.OK, "the account must be usable before it is locked");

        // Act — exhaust the failure counter. Every one of these is a 400 invalid_grant, both before
        // and after the threshold, so counting responses here proves nothing on its own.
        for (int attempt = 0; attempt < MaxFailedAccessAttempts; attempt++)
        {
            HttpResponseMessage failure = await RequestTokenAsync(email, "Wrong-P@ssw0rd-000001");

            failure.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // Assert — the correct password, which succeeded a moment ago, is now refused. That is the
        // only observable difference between a locked account and a merely mistyped one, and it is
        // the property worth having: without it the token endpoint is an unmetered password oracle
        // that a distributed attacker can hammer past the Gateway's per-IP window entirely.
        HttpResponseMessage afterLockout = await RequestTokenAsync(email, StrongPassword);

        afterLockout.StatusCode.Should().Be(
            HttpStatusCode.BadRequest,
            "five failed attempts must lock the account, so even the correct password is refused " +
            "until DefaultLockoutTimeSpan elapses");
    }

    [Fact]
    public async Task TokenEndpoint_Should_NotRevealWhetherTheAccountExists()
    {
        // Arrange — one registered account and one address that was never registered.
        string registered = UniqueEmail();
        await RegisterAsync(registered);

        // Act
        HttpResponseMessage wrongPassword = await RequestTokenAsync(registered, "Wrong-P@ssw0rd-000002");
        HttpResponseMessage unknownAccount = await RequestTokenAsync(UniqueEmail(), StrongPassword);

        // Assert — same status for both. The two failures are indistinguishable to a caller, which
        // is what stops the token endpoint from being an account-enumeration oracle; it is also the
        // reason the lockout test above has to prove itself through the correct password.
        wrongPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknownAccount.StatusCode.Should().Be(wrongPassword.StatusCode);
    }

    private async Task RegisterAsync(string email)
    {
        var request = new RegisterUser.Request
        {
            Email = email,
            Password = StrongPassword,
            FirstName = Faker.Name.FirstName(),
            LastName = Faker.Name.LastName(),
        };

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/register",
            request,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> RequestTokenAsync(string email, string password)
    {
        using var client = new HttpClient();

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("client_id", PublicClientId),
            new KeyValuePair<string, string>("scope", "openid profile email fooddeliveryservice.api"),
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("username", email),
            new KeyValuePair<string, string>("password", password)
        ]);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(TokenEndpoint))
        {
            Content = content
        };

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
