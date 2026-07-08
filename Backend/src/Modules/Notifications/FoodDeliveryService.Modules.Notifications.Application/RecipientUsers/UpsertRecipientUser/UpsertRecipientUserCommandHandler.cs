using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;

namespace FoodDeliveryService.Modules.Notifications.Application.RecipientUsers.UpsertRecipientUser;

internal sealed class UpsertRecipientUserCommandHandler(
    IRecipientUserRepository recipientUserRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpsertRecipientUserCommand>
{
    public async Task<Result> Handle(UpsertRecipientUserCommand request, CancellationToken cancellationToken)
    {
        RecipientUser? recipientUser = await recipientUserRepository.GetAsync(request.UserId, cancellationToken);

        if (recipientUser is null)
        {
            recipientUserRepository.Insert(
                RecipientUser.Create(request.UserId, request.Email, request.FirstName, request.LastName));
        }
        else
        {
            recipientUser.Update(request.FirstName, request.LastName);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
