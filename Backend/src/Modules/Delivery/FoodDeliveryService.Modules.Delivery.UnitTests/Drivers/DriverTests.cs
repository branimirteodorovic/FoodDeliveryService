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

    [Fact]
    public void GoAvailable_ShouldMoveOfflineDriverToAvailable_AndRaiseEvent()
    {
        // Arrange
        Driver driver = OnboardDriver();
        driver.ClearDomainEvents();

        // Act
        Result result = driver.GoAvailable();

        // Assert
        result.IsSuccess.Should().BeTrue();
        driver.Status.Should().Be(DriverStatus.Available);

        DriverBecameAvailableDomainEvent domainEvent =
            AssertDomainEventWasPublished<DriverBecameAvailableDomainEvent>(driver);
        domainEvent.DriverId.Should().Be(driver.Id);
    }

    [Fact]
    public void GoAvailable_ShouldFail_WhenAlreadyAvailable()
    {
        // Arrange
        Driver driver = Available();
        driver.ClearDomainEvents();

        // Act
        Result result = driver.GoAvailable();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DriverErrors.InvalidStatusTransition(DriverStatus.Available, DriverStatus.Available));
        driver.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void GoOffline_ShouldMoveAvailableDriverToOffline_AndRaiseEvent()
    {
        // Arrange
        Driver driver = Available();
        driver.ClearDomainEvents();

        // Act
        Result result = driver.GoOffline();

        // Assert
        result.IsSuccess.Should().BeTrue();
        driver.Status.Should().Be(DriverStatus.Offline);

        DriverWentOfflineDomainEvent domainEvent =
            AssertDomainEventWasPublished<DriverWentOfflineDomainEvent>(driver);
        domainEvent.DriverId.Should().Be(driver.Id);
    }

    [Fact]
    public void GoOffline_ShouldFail_WhenDriverIsBusy()
    {
        // Arrange — a Busy driver is mid-delivery and must not be able to clock off.
        Driver driver = Busy();
        driver.ClearDomainEvents();

        // Act
        Result result = driver.GoOffline();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DriverErrors.OnDelivery);
        driver.Status.Should().Be(DriverStatus.Busy);
        driver.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void GoOffline_ShouldFail_WhenAlreadyOffline()
    {
        // Arrange
        Driver driver = OnboardDriver();
        driver.ClearDomainEvents();

        // Act
        Result result = driver.GoOffline();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DriverErrors.InvalidStatusTransition(DriverStatus.Offline, DriverStatus.Offline));
        driver.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Reserve_ShouldMoveAvailableDriverToBusy_AndRaiseEvent()
    {
        // Arrange
        Driver driver = Available();
        driver.ClearDomainEvents();

        // Act
        Result result = driver.Reserve();

        // Assert
        result.IsSuccess.Should().BeTrue();
        driver.Status.Should().Be(DriverStatus.Busy);

        DriverReservedDomainEvent domainEvent =
            AssertDomainEventWasPublished<DriverReservedDomainEvent>(driver);
        domainEvent.DriverId.Should().Be(driver.Id);
    }

    [Fact]
    public void Reserve_ShouldFail_WhenDriverIsNotAvailable()
    {
        // Arrange — the second delivery to grab an already-reserved (Busy) driver must fail; this is
        // the aggregate-level guard against two orders taking the same driver.
        Driver driver = Busy();
        driver.ClearDomainEvents();

        // Act
        Result result = driver.Reserve();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DriverErrors.InvalidStatusTransition(DriverStatus.Busy, DriverStatus.Busy));
        driver.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Release_ShouldMoveBusyDriverToAvailable_AndRaiseEvent()
    {
        // Arrange
        Driver driver = Busy();
        driver.ClearDomainEvents();

        // Act
        Result result = driver.Release();

        // Assert
        result.IsSuccess.Should().BeTrue();
        driver.Status.Should().Be(DriverStatus.Available);

        DriverReleasedDomainEvent domainEvent =
            AssertDomainEventWasPublished<DriverReleasedDomainEvent>(driver);
        domainEvent.DriverId.Should().Be(driver.Id);
    }

    [Fact]
    public void Release_ShouldFail_WhenDriverIsNotBusy()
    {
        // Arrange
        Driver driver = Available();
        driver.ClearDomainEvents();

        // Act
        Result result = driver.Release();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DriverErrors.InvalidStatusTransition(DriverStatus.Available, DriverStatus.Available));
        driver.DomainEvents.Should().BeEmpty();
    }

    private static Driver Available()
    {
        Driver driver = OnboardDriver();
        driver.GoAvailable();
        return driver;
    }

    private static Driver Busy()
    {
        Driver driver = Available();
        driver.Reserve();
        return driver;
    }
}
