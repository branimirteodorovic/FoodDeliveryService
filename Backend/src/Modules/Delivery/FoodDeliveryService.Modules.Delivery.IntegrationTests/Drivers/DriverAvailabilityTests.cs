using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Delivery.Application.Drivers.GetDriver;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Delivery.Presentation.Drivers;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Drivers;

/// <summary>
/// The availability endpoint driving the driver's Offline ↔ Available transitions through the full
/// pipeline, plus its self-only authorization.
/// </summary>
public class DriverAvailabilityTests : BaseIntegrationTest
{
    public DriverAvailabilityTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task SetAvailability_Should_MoveDriverBetweenOfflineAndAvailable()
    {
        // Arrange — an activated driver starts Offline.
        HttpClient adminClient = await CreateAdminClientAsync();
        (_, string email) = await OnboardDriverAsync(adminClient);
        await ActivateDriverAsync(email, Factory.TestUserPassword);
        HttpClient driverClient = await CreateClientForUserAsync(email, Factory.TestUserPassword);

        // Act — go available.
        HttpResponseMessage goAvailable = await driverClient.PatchAsJsonAsync(
            "delivery/drivers/me/availability",
            new SetMyAvailability.Request { Available = true },
            TestContext.Current.CancellationToken);

        // Assert
        goAvailable.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetOwnStatusAsync(driverClient)).Should().Be(DriverStatus.Available);

        // Act — go back offline.
        HttpResponseMessage goOffline = await driverClient.PatchAsJsonAsync(
            "delivery/drivers/me/availability",
            new SetMyAvailability.Request { Available = false },
            TestContext.Current.CancellationToken);

        // Assert
        goOffline.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetOwnStatusAsync(driverClient)).Should().Be(DriverStatus.Offline);
    }

    [Fact]
    public async Task SetAvailability_Should_ReturnForbidden_WhenCallerIsNotADriver()
    {
        // Arrange — the customer holds no drivers:update permission.
        HttpClient customerClient = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await customerClient.PatchAsJsonAsync(
            "delivery/drivers/me/availability",
            new SetMyAvailability.Request { Available = true },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<DriverStatus> GetOwnStatusAsync(HttpClient driverClient)
    {
        DriverResponse? profile = await driverClient.GetFromJsonAsync<DriverResponse>(
            new Uri("delivery/drivers/me", UriKind.Relative),
            TestContext.Current.CancellationToken);

        profile.Should().NotBeNull();

        return profile!.Status;
    }
}
