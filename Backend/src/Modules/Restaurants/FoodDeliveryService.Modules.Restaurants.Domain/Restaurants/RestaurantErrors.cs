using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public static class RestaurantErrors
{
    public static Error NotFound(Guid restaurantId) =>
        Error.NotFound(
            "Restaurants.NotFound",
            $"The restaurant with the identifier {restaurantId} was not found");

    // A manager may own multiple restaurants — there is no one-per-manager uniqueness rule; this
    // error only guards writes against non-owning managers (administrators bypass it).
    public static readonly Error NotManager = Error.Problem(
        "Restaurants.NotManager",
        "Only the restaurant's manager or an administrator can modify this restaurant");

    public static readonly Error InvalidCommissionRate = Error.Problem(
        "Restaurants.InvalidCommissionRate",
        "The commission rate must be a fraction greater than or equal to 0 and less than 1");

    // A restaurant without coordinates can never be assigned a driver, so latitude/longitude are
    // required at onboarding (Delivery Feature 2.1).
    public static readonly Error MissingCoordinates = Error.Problem(
        "Restaurants.MissingCoordinates",
        "The restaurant address must include a latitude and a longitude");

    public static readonly Error InvalidCoordinates = Error.Problem(
        "Restaurants.InvalidCoordinates",
        "The latitude must be between -90 and 90 and the longitude between -180 and 180");
}
