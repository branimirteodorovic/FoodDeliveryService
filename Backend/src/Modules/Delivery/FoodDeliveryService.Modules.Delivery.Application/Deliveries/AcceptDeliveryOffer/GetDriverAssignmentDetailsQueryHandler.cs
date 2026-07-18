using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.AcceptDeliveryOffer;

internal sealed class GetDriverAssignmentDetailsQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<GetDriverAssignmentDetailsQuery, DriverAssignmentDetailsResponse>
{
    public async Task<Result<DriverAssignmentDetailsResponse>> Handle(
        GetDriverAssignmentDetailsQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT
                 id AS {nameof(DriverAssignmentDetailsResponse.Id)},
                 first_name AS {nameof(DriverAssignmentDetailsResponse.FirstName)},
                 last_name AS {nameof(DriverAssignmentDetailsResponse.LastName)},
                 vehicle_type AS {nameof(DriverAssignmentDetailsResponse.VehicleType)}
             FROM drivers
             WHERE id = @DriverId
             """;

        DriverAssignmentDetailsResponse? driver =
            await connection.QuerySingleOrDefaultAsync<DriverAssignmentDetailsResponse>(
                sql,
                new { request.DriverId });

        if (driver is null)
        {
            return Result.Failure<DriverAssignmentDetailsResponse>(DriverErrors.NotFound(request.DriverId));
        }

        return driver;
    }
}
