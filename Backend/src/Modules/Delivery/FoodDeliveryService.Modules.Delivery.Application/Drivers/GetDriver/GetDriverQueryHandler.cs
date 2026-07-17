using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.GetDriver;

internal sealed class GetDriverQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IDeliveryContext deliveryContext)
    : IQueryHandler<GetDriverQuery, DriverResponse>
{
    public async Task<Result<DriverResponse>> Handle(GetDriverQuery request, CancellationToken cancellationToken)
    {
        Guid driverId = request.DriverId ?? deliveryContext.UserId;

        // Self-or-admin: an explicit id other than the caller's own requires the admin-only
        // deliveries:administer permission (drivers all hold drivers:read for their own profile).
        if (driverId != deliveryContext.UserId &&
            !deliveryContext.HasPermission(Permissions.AdministerDeliveries))
        {
            return Result.Failure<DriverResponse>(DriverErrors.NotSelf);
        }

        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT
                 id AS {nameof(DriverResponse.Id)},
                 email AS {nameof(DriverResponse.Email)},
                 first_name AS {nameof(DriverResponse.FirstName)},
                 last_name AS {nameof(DriverResponse.LastName)},
                 vehicle_type AS {nameof(DriverResponse.VehicleType)},
                 status AS {nameof(DriverResponse.Status)},
                 onboarded_on_utc AS {nameof(DriverResponse.OnboardedOnUtc)}
             FROM drivers
             WHERE id = @DriverId
             """;

        DriverResponse? driver = await connection.QuerySingleOrDefaultAsync<DriverResponse>(
            sql,
            new { DriverId = driverId });

        if (driver is null)
        {
            return Result.Failure<DriverResponse>(DriverErrors.NotFound(driverId));
        }

        return driver;
    }
}
