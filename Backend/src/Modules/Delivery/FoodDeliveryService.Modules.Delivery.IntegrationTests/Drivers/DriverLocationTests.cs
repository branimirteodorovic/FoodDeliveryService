using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Delivery.Presentation.Drivers;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Drivers;

/// <summary>
/// The location endpoint through the full pipeline: an available driver's report is stored and
/// read back, an offline driver is refused, a non-driver is forbidden, and every accepted report
/// leaves a history row.
/// </summary>
public class DriverLocationTests : BaseIntegrationTest
{
    private const double Latitude = 44.8176;
    private const double Longitude = 20.4633;

    public DriverLocationTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task RecordLocation_Should_StoreCurrentPosition_AndAppendHistory()
    {
        // Arrange — an available driver (offline drivers can't report).
        HttpClient adminClient = await CreateAdminClientAsync();
        (Guid driverId, string email) = await OnboardDriverAsync(adminClient);
        await ActivateDriverAsync(email, Factory.TestUserPassword);
        HttpClient driverClient = await CreateClientForUserAsync(email, Factory.TestUserPassword);

        await GoAvailableAsync(driverClient);

        // Act
        HttpResponseMessage response = await driverClient.PostAsJsonAsync(
            "delivery/drivers/me/location",
            new RecordMyLocation.Request { Latitude = Latitude, Longitude = Longitude },
            TestContext.Current.CancellationToken);

        // Assert — 204, the live position is readable, and history gained a row.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var locationStore = scope.ServiceProvider.GetRequiredService<IDriverLocationStore>();
        DriverLocation? current = await locationStore.GetCurrentAsync(
            driverId,
            TestContext.Current.CancellationToken);

        current.Should().NotBeNull();
        current!.Location.Latitude.Should().BeApproximately(Latitude, 0.0001);
        current.Location.Longitude.Should().BeApproximately(Longitude, 0.0001);

        (await CountHistoryRowsAsync(scope, driverId)).Should().Be(1);
    }

    [Fact]
    public async Task RecordLocation_Should_Fail_ForAnOfflineDriver()
    {
        // Arrange — activated but never went available, so still Offline.
        HttpClient adminClient = await CreateAdminClientAsync();
        (_, string email) = await OnboardDriverAsync(adminClient);
        await ActivateDriverAsync(email, Factory.TestUserPassword);
        HttpClient driverClient = await CreateClientForUserAsync(email, Factory.TestUserPassword);

        // Act
        HttpResponseMessage response = await driverClient.PostAsJsonAsync(
            "delivery/drivers/me/location",
            new RecordMyLocation.Request { Latitude = Latitude, Longitude = Longitude },
            TestContext.Current.CancellationToken);

        // Assert — the Drivers.Offline problem, mapped to 400.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RecordLocation_Should_ReturnForbidden_WhenCallerIsNotADriver()
    {
        // Arrange — the customer holds no drivers:update permission.
        HttpClient customerClient = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await customerClient.PostAsJsonAsync(
            "delivery/drivers/me/location",
            new RecordMyLocation.Request { Latitude = Latitude, Longitude = Longitude },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task GoAvailableAsync(HttpClient driverClient)
    {
        HttpResponseMessage response = await driverClient.PatchAsJsonAsync(
            "delivery/drivers/me/availability",
            new SetMyAvailability.Request { Available = true },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<int> CountHistoryRowsAsync(AsyncServiceScope scope, Guid driverId)
    {
        var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM driver_location_history WHERE driver_id = @DriverId",
            new { DriverId = driverId });
    }
}
