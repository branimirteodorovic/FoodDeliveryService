using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Users.RegisterUser;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using MediatR;

namespace FoodDeliveryService.Modules.Users.Presentation.Users;

/// <summary>
/// Synchronous entry point the Restaurants module calls to provision a manager account (mirrors
/// GetUserPermissionsRequestConsumer). Runs RegisterUserCommand as an invited RestaurantManager and
/// replies with the new UserId, or the Error so duplicate-email/validation failures surface to the
/// caller as a proper failure instead of a 500.
/// </summary>
public sealed class ProvisionManagerUserRequestConsumer(ISender sender)
    : IConsumer<ProvisionManagerUserRequest>
{
    public async Task Consume(ConsumeContext<ProvisionManagerUserRequest> context)
    {
        var command = new RegisterUserCommand(
            context.Message.Email,
            Password: string.Empty,
            context.Message.FirstName,
            context.Message.LastName,
            Role: Role.RestaurantManager.Name,
            RequireInvitation: true);

        Result<Guid> result = await sender.Send(command);

        if (result.IsSuccess)
        {
            await context.RespondAsync(new ProvisionManagerUserResponse(result.Value));
        }
        else
        {
            await context.RespondAsync(result.Error);
        }
    }
}
