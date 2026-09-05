using FluentValidation;
using FoodDeliveryService.Modules.Users.Application.Users.RegisterUser;

namespace FoodDeliveryService.Modules.Users.Application.Users.AcceptInvitation;

internal sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        // Feature 3.7 Milestone F. Anonymous, like registration, and bounded for the same reason.
        // The token is an ASP.NET Identity password-reset token — a few hundred characters — and
        // anything longer is not a token this platform ever minted.
        RuleFor(c => c.Email).EmailAddress().MaximumLength(300);
        RuleFor(c => c.Token).NotEmpty().MaximumLength(TokenMaxLength);
        RuleFor(c => c.NewPassword)
            .MinimumLength(6)
            .MaximumLength(RegisterUserCommandValidator.PasswordMaxLength);
    }

    private const int TokenMaxLength = 2000;
}
