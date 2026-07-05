namespace FoodDeliveryService.Modules.Users.Infrastructure.Identity;

internal sealed record SetPasswordRequest(
    string Email,
    string Token,
    string NewPassword);
