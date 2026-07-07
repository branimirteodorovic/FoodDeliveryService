using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.OnboardRestaurant;

/// <summary>
/// Single admin action that onboards a restaurant: business fields (including the negotiated
/// commission rate as a fraction in [0, 1)) plus the manager's contact details. The handler
/// provisions the manager account in Users over the bus, then persists the Restaurant.
/// </summary>
public sealed record OnboardRestaurantCommand(
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
    string ManagerEmail,
    string ManagerFirstName,
    string ManagerLastName) : ICommand<Guid>;
