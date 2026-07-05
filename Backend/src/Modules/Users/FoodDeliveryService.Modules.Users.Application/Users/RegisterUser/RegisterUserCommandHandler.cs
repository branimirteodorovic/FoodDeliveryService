using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Identity;
using FoodDeliveryService.Modules.Users.Domain.Users;

namespace FoodDeliveryService.Modules.Users.Application.Users.RegisterUser;

internal sealed class RegisterUserCommandHandler(
    IIdentityProviderService identityProviderService,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RegisterUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        // Role is validated by RegisterUserCommandValidator; FromName never returns null here.
        Role role = Role.FromName(request.Role)!;

        Result<User> userResult = request.RequireInvitation
            ? await CreateInvitedUserAsync(request, role, cancellationToken)
            : await CreateSelfServiceUserAsync(request, role, cancellationToken);

        if (userResult.IsFailure)
        {
            return Result.Failure<Guid>(userResult.Error);
        }

        userRepository.Insert(userResult.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return userResult.Value.Id;
    }

    private async Task<Result<User>> CreateSelfServiceUserAsync(
        RegisterUserCommand request,
        Role role,
        CancellationToken cancellationToken)
    {
        Result<string> result = await identityProviderService.RegisterUserAsync(
            new UserModel(request.Email, request.Password, request.FirstName, request.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<User>(result.Error);
        }

        return User.Create(request.Email, request.FirstName, request.LastName, result.Value, role);
    }

    private async Task<Result<User>> CreateInvitedUserAsync(
        RegisterUserCommand request,
        Role role,
        CancellationToken cancellationToken)
    {
        // Provision the identity with no password and obtain a one-time activation token; the
        // UserInvitedDomainEvent raised by CreateInvited carries it to Notifications via the outbox.
        Result<InvitationResult> invitation = await identityProviderService.RegisterInvitedUserAsync(
            new InvitedUserModel(request.Email, request.FirstName, request.LastName),
            cancellationToken);

        if (invitation.IsFailure)
        {
            return Result.Failure<User>(invitation.Error);
        }

        return User.CreateInvited(
            request.Email,
            request.FirstName,
            request.LastName,
            invitation.Value.IdentityId,
            role,
            invitation.Value.ActivationToken,
            invitation.Value.ExpiresOnUtc);
    }
}
