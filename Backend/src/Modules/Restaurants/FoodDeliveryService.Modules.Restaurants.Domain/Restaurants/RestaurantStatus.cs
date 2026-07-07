namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

// Restaurants are Active immediately on onboarding — there is no approval step (the contract is
// handled off-platform). Suspended is reserved for later admin tooling.
public enum RestaurantStatus
{
    Active = 1,
    Suspended = 2
}
