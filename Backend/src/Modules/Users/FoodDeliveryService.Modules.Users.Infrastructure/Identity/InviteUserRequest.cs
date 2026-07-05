namespace FoodDeliveryService.Modules.Users.Infrastructure.Identity;

internal sealed record InviteUserRequest(
    string Email,
    string FirstName,
    string LastName);
