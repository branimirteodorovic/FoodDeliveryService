using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateRestaurant;

public sealed record UpdateRestaurantCommand(
    Guid RestaurantId,
    string Name,
    string TaxIdentification,
    string CuisineType,
    string Email,
    string PhoneNumber,
    string Street,
    string City,
    string PostalCode,
    string Country,
    double? Latitude,
    double? Longitude) : ICommand;
