using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Users.Application.Abstractions.Identity;

public static class IdentityProviderErrors
{
    public static readonly Error EmailIsNotUnique = Error.Conflict(
        "Identity.EmailIsNotUnique",
        "The specified email is not unique.");

    public static readonly Error InvalidActivationToken = Error.Problem(
        "Identity.InvalidActivationToken",
        "The activation link is invalid or has expired.");

    public static readonly Error AccountAlreadyActivated = Error.Conflict(
        "Identity.AccountAlreadyActivated",
        "The account has already been activated and cannot be removed by compensation.");
}
