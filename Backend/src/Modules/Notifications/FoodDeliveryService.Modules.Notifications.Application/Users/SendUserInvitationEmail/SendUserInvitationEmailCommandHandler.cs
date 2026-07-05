using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Email;

namespace FoodDeliveryService.Modules.Notifications.Application.Users.SendUserInvitationEmail;

internal sealed class SendUserInvitationEmailCommandHandler(IEmailService emailService)
    : ICommandHandler<SendUserInvitationEmailCommand>
{
    public async Task<Result> Handle(SendUserInvitationEmailCommand request, CancellationToken cancellationToken)
    {
        await emailService.SendInvitationEmailAsync(
            request.Email,
            request.FirstName,
            request.ActivationToken,
            request.ExpiresOnUtc,
            cancellationToken);

        return Result.Success();
    }
}
