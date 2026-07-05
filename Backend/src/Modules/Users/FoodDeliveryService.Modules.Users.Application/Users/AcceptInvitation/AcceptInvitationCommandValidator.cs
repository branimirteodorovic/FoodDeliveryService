using FluentValidation;

namespace FoodDeliveryService.Modules.Users.Application.Users.AcceptInvitation;

internal sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        RuleFor(c => c.Email).EmailAddress();
        RuleFor(c => c.Token).NotEmpty();
        RuleFor(c => c.NewPassword).MinimumLength(6);
    }
}
