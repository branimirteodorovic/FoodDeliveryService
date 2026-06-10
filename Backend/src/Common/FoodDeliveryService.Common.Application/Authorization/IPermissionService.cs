using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Common.Application.Authorization;

public interface IPermissionService
{
    Task<Result<PermissionsResponse>> GetUserPermissionsAsync(string identityId);
}
