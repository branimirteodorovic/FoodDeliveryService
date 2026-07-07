using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;

// Response DTO — domain entities are never exposed in API responses.
public sealed record RestaurantResponse(
    Guid Id,
    Guid ManagerUserId,
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
    double? Longitude,
    decimal CommissionRate,
    RestaurantStatus Status,
    DateTime CreatedOnUtc);
