using FluentValidation;
using FoodDeliveryService.Modules.Users.Domain.Users;

namespace FoodDeliveryService.Modules.Users.Application.Users.RegisterUser;

internal sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(c => c.FirstName).NotEmpty();
        RuleFor(c => c.LastName).NotEmpty();
        RuleFor(c => c.Email).EmailAddress();

        // Role must be one of the assignable roles (Customer, RestaurantManager, …) — Administrator
        // is not assignable via registration/provisioning.
        RuleFor(c => c.Role)
            .Must(role => Role.FromName(role) is not null)
            .WithMessage("The specified role is not valid.");

        // Self-service registration needs a password; invited accounts must NOT carry one (the
        // invitee sets it later via the activation link).
        When(c => c.RequireInvitation, () =>
        {
            RuleFor(c => c.Password)
                .Empty()
                .WithMessage("Invited accounts must not supply a password.");
        }).Otherwise(() =>
        {
            RuleFor(c => c.Password).MinimumLength(6);
        });
    }
}
