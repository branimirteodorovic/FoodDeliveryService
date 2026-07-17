using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Drivers;

/// <summary>
/// Aggregate root for a delivery driver. The driver's profile IS the aggregate, keyed by the
/// Users service's UserId — there is no separate user replica (a Restaurant and its manager are
/// different things; a Driver and their user account are the same thing). Created by an
/// Administrator during onboarding via the ProvisionUserRequest RPC to Users; drivers never
/// self-register. Email/name are snapshots kept in sync from UserProfileUpdated integration
/// events.
/// </summary>
public sealed class Driver : Entity
{
    private Driver()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public VehicleType VehicleType { get; private set; }

    public DriverStatus Status { get; private set; }

    public DateTime OnboardedOnUtc { get; private set; }

    public static Result<Driver> Onboard(
        Guid userId,
        string email,
        string firstName,
        string lastName,
        VehicleType vehicleType,
        DateTime utcNow)
    {
        if (!Enum.IsDefined(vehicleType))
        {
            return Result.Failure<Driver>(DriverErrors.InvalidVehicleType);
        }

        var driver = new Driver
        {
            Id = userId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            VehicleType = vehicleType,
            Status = DriverStatus.Offline,
            OnboardedOnUtc = utcNow
        };

        driver.Raise(new DriverOnboardedDomainEvent(driver.Id));

        return driver;
    }

    /// <summary>
    /// The driver edits their own name/vehicle. Name changes here are module-local snapshots;
    /// the Users service remains the owner of the canonical profile.
    /// </summary>
    public Result UpdateProfile(string firstName, string lastName, VehicleType vehicleType)
    {
        if (!Enum.IsDefined(vehicleType))
        {
            return Result.Failure(DriverErrors.InvalidVehicleType);
        }

        if (FirstName == firstName && LastName == lastName && VehicleType == vehicleType)
        {
            return Result.Success();
        }

        FirstName = firstName;
        LastName = lastName;
        VehicleType = vehicleType;

        Raise(new DriverProfileUpdatedDomainEvent(Id));

        return Result.Success();
    }

    /// <summary>
    /// Called by the UserProfileUpdated integration event handler to keep the name snapshot
    /// current. A no-op — raising NO event — when nothing changed, so replayed/duplicate events
    /// don't generate noise in the outbox.
    /// </summary>
    public void SyncFromUserProfile(string firstName, string lastName)
    {
        if (FirstName == firstName && LastName == lastName)
        {
            return;
        }

        FirstName = firstName;
        LastName = lastName;

        Raise(new DriverProfileUpdatedDomainEvent(Id));
    }
}
