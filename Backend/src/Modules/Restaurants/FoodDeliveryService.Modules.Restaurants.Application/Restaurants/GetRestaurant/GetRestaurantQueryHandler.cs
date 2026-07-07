using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;

internal sealed class GetRestaurantQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : IQueryHandler<GetRestaurantQuery, RestaurantResponse>
{
    public async Task<Result<RestaurantResponse>> Handle(GetRestaurantQuery request, CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        const string sql =
            $"""
             SELECT
                 id AS {nameof(RestaurantResponse.Id)},
                 manager_user_id AS {nameof(RestaurantResponse.ManagerUserId)},
                 name AS {nameof(RestaurantResponse.Name)},
                 tax_identification AS {nameof(RestaurantResponse.TaxIdentification)},
                 cuisine_type AS {nameof(RestaurantResponse.CuisineType)},
                 email AS {nameof(RestaurantResponse.Email)},
                 phone_number AS {nameof(RestaurantResponse.PhoneNumber)},
                 address_street AS {nameof(RestaurantResponse.Street)},
                 address_city AS {nameof(RestaurantResponse.City)},
                 address_postal_code AS {nameof(RestaurantResponse.PostalCode)},
                 address_country AS {nameof(RestaurantResponse.Country)},
                 address_latitude AS {nameof(RestaurantResponse.Latitude)},
                 address_longitude AS {nameof(RestaurantResponse.Longitude)},
                 commission_rate AS {nameof(RestaurantResponse.CommissionRate)},
                 status AS {nameof(RestaurantResponse.Status)},
                 created_on_utc AS {nameof(RestaurantResponse.CreatedOnUtc)}
             FROM restaurants
             WHERE id = @RestaurantId
             """;

        RestaurantResponse? restaurant =
            await connection.QuerySingleOrDefaultAsync<RestaurantResponse>(sql, request);

        if (restaurant is null)
        {
            return Result.Failure<RestaurantResponse>(RestaurantErrors.NotFound(request.RestaurantId));
        }

        return restaurant;
    }
}
