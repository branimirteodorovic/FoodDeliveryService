using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Users.DeactivateUser;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using MediatR;

namespace FoodDeliveryService.Modules.Users.Presentation.Users;

/// <summary>
/// Compensation entry point for a failed restaurant onboarding: removes the orphaned invited
/// manager account (module user + Identity credentials). Mirrors ProvisionManagerUserRequestConsumer;
/// failures (unknown user, already-activated account) are replied as an Error so the caller can log
/// them instead of getting a timeout.
/// </summary>
public sealed class DeactivateProvisionedUserRequestConsumer(ISender sender)
    : IConsumer<DeactivateProvisionedUserRequest>
{
    public async Task Consume(ConsumeContext<DeactivateProvisionedUserRequest> context)
    {
        Result result = await sender.Send(new DeactivateUserCommand(context.Message.UserId));

        if (result.IsSuccess)
        {
            await context.RespondAsync(new DeactivateProvisionedUserResponse(context.Message.UserId));
        }
        else
        {
            await context.RespondAsync(result.Error);
        }
    }
}
