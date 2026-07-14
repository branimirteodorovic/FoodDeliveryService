using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;

namespace FoodDeliveryService.Modules.Users.Presentation.Users;

public sealed class GetUserPermissionsRequestConsumer(IPermissionService permissionService) : IConsumer<GetUserPermissionsRequest>
{
    public async Task Consume(ConsumeContext<GetUserPermissionsRequest> context)
    {
        var result = await permissionService.GetUserPermissionsAsync(context.Message.IdentityId);

        if (result.IsSuccess)
        {
            await context.RespondAsync(result.Value);
        }
        else
        {
            await context.RespondAsync(result.Error);
        }
    }
}
