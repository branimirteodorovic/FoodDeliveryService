using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Delivery.Presentation.Drivers;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Drivers;

/// <summary>
/// Drives POST delivery/drivers through the full pipeline: real JWT (Identity on :18080), real
/// permission RPC and real ProvisionUserRequest RPC answered by the in-process Users host over the
/// ephemeral RabbitMQ broker, real Postgres on both sides.
/// </summary>
public class OnboardDriverTests : BaseIntegrationTest
{
    public OnboardDriverTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task OnboardDriver_Should_CreateDriverAndInvitedUsersAccount_WhenCallerIsAdministrator()
    {
        // Arrange
        HttpClient adminClient = await CreateAdminClientAsync();

        // Act
        (Guid driverId, string email) = await OnboardDriverAsync(adminClient, vehicleType: "Car");

        // Assert — the Driver row exists in the Delivery database, keyed by the provisioned UserId,
        // Offline until the driver goes available (Milestone C).
        await using AsyncServiceScope deliveryScope = Factory.Services.CreateAsyncScope();
        var driversRepository = deliveryScope.ServiceProvider.GetRequiredService<IDriversRepository>();

        Driver? driver = await driversRepository.GetAsync(driverId, TestContext.Current.CancellationToken);

        driver.Should().NotBeNull();
        driver!.Email.Should().Be(email);
        driver.VehicleType.Should().Be(VehicleType.Car);
        driver.Status.Should().Be(DriverStatus.Offline);

        // …and the module-side Users account was provisioned with the same id.
        await using AsyncServiceScope usersScope = Factory.UsersApi.Services.CreateAsyncScope();
        var userRepository = usersScope.ServiceProvider
            .GetRequiredService<FoodDeliveryService.Modules.Users.Domain.Users.IUserRepository>();

        FoodDeliveryService.Modules.Users.Domain.Users.User? user =
            await userRepository.GetAsync(driverId, TestContext.Current.CancellationToken);

        user.Should().NotBeNull();
        user!.Email.Should().Be(email);

        // The account is invited (no password yet) — logging in must fail until activation.
        using HttpResponseMessage tokenResponse = await RequestTokenAsync(email, Factory.TestUserPassword);
        tokenResponse.IsSuccessStatusCode.Should().BeFalse("an invited account has no password until activation");
    }

    [Fact]
    public async Task OnboardDriver_Should_ReturnForbidden_WhenCallerIsCustomer()
    {
        // Arrange — customers hold no users:provision permission.
        HttpClient customerClient = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await customerClient.PostAsJsonAsync(
            "delivery/drivers",
            new OnboardDriver.Request
            {
                Email = $"driver+{Guid.NewGuid():N}@fooddeliveryservice.com",
                FirstName = Faker.Name.FirstName(),
                LastName = Faker.Name.LastName(),
                VehicleType = "Bicycle",
            },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OnboardDriver_Should_ReturnUnauthorized_WhenAnonymous()
    {
        // Act — HttpClient carries no Bearer token.
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "delivery/drivers",
            new OnboardDriver.Request
            {
                Email = $"driver+{Guid.NewGuid():N}@fooddeliveryservice.com",
                FirstName = Faker.Name.FirstName(),
                LastName = Faker.Name.LastName(),
                VehicleType = "Bicycle",
            },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OnboardDriver_Should_ReturnBadRequest_WhenVehicleTypeIsUnknown()
    {
        // Arrange
        HttpClient adminClient = await CreateAdminClientAsync();

        // Act — fails validation before any RPC is sent, so no orphan account is provisioned.
        HttpResponseMessage response = await adminClient.PostAsJsonAsync(
            "delivery/drivers",
            new OnboardDriver.Request
            {
                Email = $"driver+{Guid.NewGuid():N}@fooddeliveryservice.com",
                FirstName = Faker.Name.FirstName(),
                LastName = Faker.Name.LastName(),
                VehicleType = "Rollerblades",
            },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OnboardDriver_Should_FailCleanly_WhenEmailIsDuplicate()
    {
        // Arrange — provision once, then re-provision the same email. Identity rejects the
        // duplicate; the RPC relays it as an Error response and the endpoint surfaces a clean
        // client error, never a 500/timeout.
        HttpClient adminClient = await CreateAdminClientAsync();
        (_, string email) = await OnboardDriverAsync(adminClient);

        // Act
        HttpResponseMessage response = await adminClient.PostAsJsonAsync(
            "delivery/drivers",
            new OnboardDriver.Request
            {
                Email = email,
                FirstName = Faker.Name.FirstName(),
                LastName = Faker.Name.LastName(),
                VehicleType = "Bicycle",
            },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
    }
}
