using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Drivers.GetDriver;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Delivery.Presentation.Drivers;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Drivers;

/// <summary>
/// The invited driver's lifecycle after onboarding: activation via the real accept-invitation
/// endpoint, self-scoped reads/updates with a real driver JWT, the self-or-admin ownership check,
/// and the UserProfileUpdated consumer keeping the name snapshot in sync over the real broker.
/// </summary>
public class DriverProfileTests : BaseIntegrationTest
{
    public DriverProfileTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task InvitedDriver_Should_ActivateLogIn_AndReadOwnProfile()
    {
        // Arrange — onboard, then activate the invitation exactly like the emailed link would.
        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, string email) = await OnboardDriverAsync(adminClient, vehicleType: "Motorcycle");

        await ActivateDriverAsync(email, Factory.TestUserPassword);

        // Act — the activated driver logs in with their new password and reads their own profile.
        HttpClient driverClient = await CreateClientForUserAsync(email, Factory.TestUserPassword);

        HttpResponseMessage response = await driverClient.GetAsync(
            new Uri("delivery/drivers/me", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        DriverResponse? profile =
            await response.Content.ReadFromJsonAsync<DriverResponse>(TestContext.Current.CancellationToken);

        profile.Should().NotBeNull();
        profile!.Id.Should().Be(driverId);
        profile.Email.Should().Be(email);
        profile.VehicleType.Should().Be(VehicleType.Motorcycle);
        profile.Status.Should().Be(DriverStatus.Offline);
    }

    [Fact]
    public async Task GetDriver_Should_RejectAnotherDriversProfile_ButAllowAdmin()
    {
        // Arrange — two drivers; A is activated, B is only onboarded (its id is all A needs).
        HttpClient adminClient = await CreateAdminClientAsync();
        (_, string emailA) = await OnboardDriverAsync(adminClient);
        (Guid driverIdB, _) = await OnboardDriverAsync(adminClient);

        await ActivateDriverAsync(emailA, Factory.TestUserPassword);
        HttpClient driverAClient = await CreateClientForUserAsync(emailA, Factory.TestUserPassword);

        // Act — driver A reads driver B's profile.
        HttpResponseMessage driverResponse = await driverAClient.GetAsync(
            new Uri($"delivery/drivers/{driverIdB}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — refused with the NotSelf problem (mapped to 400 by ApiResults, the same shape
        // as Restaurants' NotManager ownership failure).
        driverResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // …while the admin's deliveries:administer bypasses the self-only check.
        HttpResponseMessage adminResponse = await adminClient.GetAsync(
            new Uri($"delivery/drivers/{driverIdB}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateMyDriverProfile_Should_UpdateNameAndVehicle()
    {
        // Arrange
        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, string email) = await OnboardDriverAsync(adminClient, vehicleType: "Bicycle");

        await ActivateDriverAsync(email, Factory.TestUserPassword);
        HttpClient driverClient = await CreateClientForUserAsync(email, Factory.TestUserPassword);

        // Act
        HttpResponseMessage response = await driverClient.PutAsJsonAsync(
            "delivery/drivers/me",
            new UpdateMyDriverProfile.Request
            {
                FirstName = "Updated",
                LastName = "Driver",
                VehicleType = "Car",
            },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        DriverResponse? profile = await driverClient.GetFromJsonAsync<DriverResponse>(
            new Uri("delivery/drivers/me", UriKind.Relative),
            TestContext.Current.CancellationToken);

        profile.Should().NotBeNull();
        profile!.Id.Should().Be(driverId);
        profile.FirstName.Should().Be("Updated");
        profile.LastName.Should().Be("Driver");
        profile.VehicleType.Should().Be(VehicleType.Car);
    }

    [Fact]
    public async Task UpdateMyDriverProfile_Should_ReturnForbidden_WhenCallerIsNotADriver()
    {
        // Arrange — the customer holds no drivers:update permission, so the policy rejects the
        // request before the handler's NotOnboarded check is ever reached.
        HttpClient customerClient = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await customerClient.PutAsJsonAsync(
            "delivery/drivers/me",
            new UpdateMyDriverProfile.Request
            {
                FirstName = "Not",
                LastName = "ADriver",
                VehicleType = "Car",
            },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UserProfileUpdated_Should_SyncDriverNameSnapshot()
    {
        // Arrange
        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, _) = await OnboardDriverAsync(adminClient);

        // Act — publish the Users-owned event over the real broker; Delivery's consumer writes it
        // to the inbox and ProcessInboxJob dispatches the sync handler.
        var eventBus = Factory.Services.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(
            new UserProfileUpdatedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                driverId,
                "Synced",
                "FromUsers"),
            TestContext.Current.CancellationToken);

        // Assert — poll until the inbox round-trip lands.
        Result<Driver> synced = await Poller.WaitAsync(TimeSpan.FromSeconds(30), async () =>
        {
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            var driversRepository = scope.ServiceProvider.GetRequiredService<IDriversRepository>();

            Driver? driver = await driversRepository.GetAsync(driverId, TestContext.Current.CancellationToken);

            return driver is { FirstName: "Synced", LastName: "FromUsers" }
                ? Result.Success(driver)
                : Result.Failure<Driver>(DriverErrors.NotFound(driverId));
        });

        synced.IsSuccess.Should().BeTrue("the UserProfileUpdated event must sync the driver's name snapshot");
    }
}
