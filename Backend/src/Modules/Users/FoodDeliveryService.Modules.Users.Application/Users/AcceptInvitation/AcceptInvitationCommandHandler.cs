using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Identity;

namespace FoodDeliveryService.Modules.Users.Application.Users.AcceptInvitation;

internal sealed class AcceptInvitationCommandHandler(IIdentityProviderService identityProviderService)
    : ICommandHandler<AcceptInvitationCommand>
{
    public Task<Result> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken) =>
        identityProviderService.SetPasswordAsync(
            request.Email,
            request.Token,
            request.NewPassword,
            cancellationToken);
}
