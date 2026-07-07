using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Identity;
using FoodDeliveryService.Modules.Users.Domain.Users;

namespace FoodDeliveryService.Modules.Users.Application.Users.DeactivateUser;

internal sealed class DeactivateUserCommandHandler(
    IIdentityProviderService identityProviderService,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeactivateUserCommand>
{
    public async Task<Result> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        User? user = await userRepository.GetAsync(request.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(request.UserId));
        }

        // Identity first: it refuses activated accounts, in which case the module-side user must
        // stay too. Deleting the credentials before the module row means a crash in between leaves
        // an unusable (password-less) module row rather than live credentials without a user.
        Result identityResult = await identityProviderService.DeleteInvitedUserAsync(
            user.IdentityId,
            cancellationToken);

        if (identityResult.IsFailure)
        {
            return identityResult;
        }

        // Hard delete without a domain event: the account never activated and this unwinds its
        // registration; the UserRegistered/UserInvited events already in flight point at an
        // account that no longer exists, which downstream consumers tolerate (the invitation
        // token can no longer be redeemed).
        userRepository.Remove(user);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
