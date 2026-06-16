using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Users.GetUserPermissions;
using MediatR;

namespace FoodDeliveryService.Modules.Users.Infrastructure.Authorization;

internal sealed class PermissionService(ISender sender) : IPermissionService
{
    public async Task<Result<PermissionsResponse>> GetUserPermissionsAsync(string identityId)
    {
        return await sender.Send(new GetUserPermissionsQuery(identityId));
    }
}
