namespace FoodDeliveryService.Modules.Users.Infrastructure.Identity;

internal sealed record RegisterUserRequest(
    string Email,
    string FirstName,
    string LastName,
    string Password);
