using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Users.Presentation.Users;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.AcceptInvitations;

public class AcceptInvitationTests : BaseIntegrationTest
{
    public AcceptInvitationTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AcceptInvitation_Should_ReturnBadRequest_WhenTokenInvalid()
    {
        // Arrange — no invitation was ever issued for this account, so the token is bogus. The
        // endpoint is anonymous (the account has no session yet), so no auth header is attached.
        var request = new AcceptInvitation.Request
        {
            Email = UniqueEmail(),
            Token = Guid.NewGuid().ToString("N"),
            NewPassword = StrongPassword,
        };

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/accept-invitation",
            request,
            TestContext.Current.CancellationToken);

        // Assert — an invalid/expired token is rejected as a clean 400, never a 500.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
