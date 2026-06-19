using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Authorization;

internal sealed class PermissionService(
    IRequestClient<GetUserPermissionsRequest> requestClient,
    ICacheService cacheService) : IPermissionService
{
    private static readonly Error NotFound = Error.NotFound(nameof(PermissionService), "User user was not found.");
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    public async Task<Result<PermissionsResponse>> GetUserPermissionsAsync(string identityId)
    {
        PermissionsResponse? permissionsResponse = await cacheService.GetAsync<PermissionsResponse>(CreateCacheKey(identityId));

        if (permissionsResponse is not null)
        {
            return permissionsResponse;
        }

        var request = new GetUserPermissionsRequest(identityId);

        var response = await requestClient.GetResponse<PermissionsResponse, Error>(request);

        if (response.Is(out Response<Error> errorResponse))
        {
            return Result.Failure<PermissionsResponse>(errorResponse.Message);
        }

        if (response.Is(out Response<PermissionsResponse> permissionResponse))
        {
            await cacheService.SetAsync(CreateCacheKey(identityId), permissionResponse.Message, CacheExpiration);
            return permissionResponse.Message;
        }

        return Result.Failure<PermissionsResponse>(NotFound);
    }

    private static string CreateCacheKey(string identityId) => $"user_permissions:{identityId}";
}
