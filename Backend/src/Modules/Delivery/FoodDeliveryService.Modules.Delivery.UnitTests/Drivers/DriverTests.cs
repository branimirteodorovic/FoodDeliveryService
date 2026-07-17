using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Delivery.UnitTests.Drivers;

public class DriverTests : BaseTest
{
    private static Driver OnboardDriver(
        Guid? userId = null,
        string? firstName = null,
        string? lastName = null,
        VehicleType vehicleType = VehicleType.Bicycle)
    {
        Result<Driver> result = Driver.Onboard(
            userId ?? Guid.NewGuid(),
            Faker.Person.Email,
            firstName ?? Faker.Person.FirstName,
            lastName ?? Faker.Person.LastName,
            vehicleType,
            DateTime.UtcNow);

        return result.Value;
    }

    [Fact]
    public void Onboard_ShouldKeyTheDriverOnTheUsersServiceUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        string email = Faker.Person.Email;
        string firstName = Faker.Person.FirstName;
        string lastName = Faker.Person.LastName;
        DateTime utcNow = DateTime.UtcNow;

        // Act
        Result<Driver> result = Driver.Onboard(userId, email, firstName, lastName, VehicleType.Car, utcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(userId);
        result.Value.Email.Should().Be(email);
        result.Value.FirstName.Should().Be(firstName);
        result.Value.LastName.Should().Be(lastName);
        result.Value.VehicleType.Should().Be(VehicleType.Car);
        result.Value.OnboardedOnUtc.Should().Be(utcNow);
    }

    [Fact]
    public void Onboard_ShouldStartTheDriverOffline()
    {
        // Arrange & Act
        Driver driver = OnboardDriver();

        // Assert
        driver.Status.Should().Be(DriverStatus.Offline);
    }

    [Fact]
    public void Onboard_ShouldRaiseDriverOnboardedDomainEvent()
    {
        // Arrange & Act
        Driver driver = OnboardDriver();

        // Assert
        DriverOnboardedDomainEvent domainEvent =
            AssertDomainEventWasPublished<DriverOnboardedDomainEvent>(driver);

        domainEvent.DriverId.Should().Be(driver.Id);
    }

    [Fact]
    public void Onboard_ShouldFail_WhenVehicleTypeIsNotDefined()
    {
        // Arrange
        const VehicleType invalidVehicleType = (VehicleType)999;

        // Act
        Result<Driver> result = Driver.Onboard(
            Guid.NewGuid(),
            Faker.Person.Email,
            Faker.Person.FirstName,
            Faker.Person.LastName,
            invalidVehicleType,
            DateTime.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DriverErrors.InvalidVehicleType);
    }

    [Fact]
    public void UpdateProfile_ShouldUpdateNameAndVehicle_AndRaiseDriverProfileUpdatedDomainEvent()
    {
        // Arrange
        Driver driver = OnboardDriver(vehicleType: VehicleType.Bicycle);
        driver.ClearDomainEvents();

        // Act
        Result result = driver.UpdateProfile("Updated First", "Updated Last", VehicleType.Motorcycle);

        // Assert
        result.IsSuccess.Should().BeTrue();
        driver.FirstName.Should().Be("Updated First");
        driver.LastName.Should().Be("Updated Last");
        driver.VehicleType.Should().Be(VehicleType.Motorcycle);

        DriverProfileUpdatedDomainEvent domainEvent =
            AssertDomainEventWasPublished<DriverProfileUpdatedDomainEvent>(driver);

        domainEvent.DriverId.Should().Be(driver.Id);
    }

    [Fact]
    public void UpdateProfile_ShouldNotRaiseDomainEvents_WhenNothingChanged()
    {
        // Arrange
        Driver driver = OnboardDriver(firstName: "First", lastName: "Last", vehicleType: VehicleType.Car);
        driver.ClearDomainEvents();

        // Act
        Result result = driver.UpdateProfile("First", "Last", VehicleType.Car);

        // Assert
        result.IsSuccess.Should().BeTrue();
        driver.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void UpdateProfile_ShouldFail_WhenVehicleTypeIsNotDefined()
    {
        // Arrange
        Driver driver = OnboardDriver();
        driver.ClearDomainEvents();

        // Act
        Result result = driver.UpdateProfile("First", "Last", (VehicleType)999);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DriverErrors.InvalidVehicleType);
        driver.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SyncFromUserProfile_ShouldUpdateNames_AndRaiseDriverProfileUpdatedDomainEvent()
    {
        // Arrange
        Driver driver = OnboardDriver(firstName: "First", lastName: "Last");
        driver.ClearDomainEvents();

        // Act
        driver.SyncFromUserProfile("Synced First", "Synced Last");

        // Assert
        driver.FirstName.Should().Be("Synced First");
        driver.LastName.Should().Be("Synced Last");

        AssertDomainEventWasPublished<DriverProfileUpdatedDomainEvent>(driver);
    }

    [Fact]
    public void SyncFromUserProfile_ShouldRaiseNothing_WhenValuesAreIdentical()
    {
        // Arrange
        Driver driver = OnboardDriver(firstName: "First", lastName: "Last");
        driver.ClearDomainEvents();

        // Act
        driver.SyncFromUserProfile("First", "Last");

        // Assert
        driver.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SyncFromUserProfile_ShouldNotChangeVehicleOrStatus()
    {
        // Arrange
        Driver driver = OnboardDriver(vehicleType: VehicleType.Motorcycle);
        driver.ClearDomainEvents();

        // Act
        driver.SyncFromUserProfile("Synced First", "Synced Last");

        // Assert
        driver.VehicleType.Should().Be(VehicleType.Motorcycle);
        driver.Status.Should().Be(DriverStatus.Offline);
    }
}
