using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;

namespace FoodDeliveryService.Modules.Notifications.Application.RecipientUsers.UpdateRecipientUser;

internal sealed class UpdateRecipientUserCommandHandler(
    IRecipientUserRepository recipientUserRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateRecipientUserCommand>
{
    public async Task<Result> Handle(UpdateRecipientUserCommand request, CancellationToken cancellationToken)
    {
        RecipientUser? recipientUser = await recipientUserRepository.GetAsync(request.UserId, cancellationToken);

        // The replica is upserted from UserRegistered; a profile update that arrives first finds no
        // row yet — treat as a no-op rather than an error (the register event will fill it in).
        if (recipientUser is null)
        {
            return Result.Success();
        }

        recipientUser.Update(request.FirstName, request.LastName);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
