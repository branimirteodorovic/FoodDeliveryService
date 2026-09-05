using FluentValidation;
using FoodDeliveryService.Modules.Users.Domain.Users;

namespace FoodDeliveryService.Modules.Users.Application.Users.RegisterUser;

internal sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        // Feature 3.7 Milestone F. Every free-text field is bounded as well as required. This is the
        // one anonymous write endpoint on the platform: without an upper bound a caller who has no
        // token at all can post megabyte names straight into a row Identity and this module both
        // persist, and a megabyte password into ASP.NET Identity's PBKDF2 hash, which is CPU the
        // edge rate limiter charges as a single request. The lengths match the columns the Users
        // module and Identity actually store.
        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Email).EmailAddress().MaximumLength(300);

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
            // The minimum stays where it was: the authoritative strength policy is ASP.NET
            // Identity's (12 characters outside Development, §6.3), and duplicating it here would
            // be a second copy to drift. The maximum is this layer's to add — Identity has none.
            RuleFor(c => c.Password).MinimumLength(6).MaximumLength(PasswordMaxLength);
        });
    }

    /// <summary>
    /// Generous enough for any passphrase a person types or a manager generates, short enough that
    /// hashing it is bounded work.
    /// </summary>
    internal const int PasswordMaxLength = 256;
}
