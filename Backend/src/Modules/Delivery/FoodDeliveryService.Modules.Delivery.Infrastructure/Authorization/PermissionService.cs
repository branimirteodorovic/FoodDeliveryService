using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Authorization;

/// <summary>
/// Resolves a user's permissions for authorization (called by CustomClaimsTransformation on each
/// authenticated request). Permissions are owned by the Users service, so this uses MassTransit
/// request/response (<see cref="IRequestClient{T}"/>) — a synchronous RPC over RabbitMQ answered
/// by GetUserPermissionsRequestConsumer in Users — with a 5-minute Redis cache in front so the
/// bus is not hit on every request.
/// </summary>
internal sealed class PermissionService(
    IRequestClient<GetUserPermissionsRequest> requestClient,
    ICacheService cacheService) : IPermissionService
{
    private static readonly Error NotFound = Error.NotFound(nameof(PermissionService), "User user was not found.");
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    public async Task<Result<PermissionsResponse>> GetUserPermissionsAsync(string identityId)
    {
        Error? failure = null;

        // The factory returns null (never cached — see GetOrCreateAsync) on an RPC failure, and
        // records the reason in the closed-over `failure` so it survives past the cache miss.
        PermissionsResponse? permissionsResponse = await cacheService.GetOrCreateAsync<PermissionsResponse?>(
            CreateCacheKey(identityId),
            async ct =>
            {
                var request = new GetUserPermissionsRequest(identityId);

                var response = await requestClient.GetResponse<PermissionsResponse, Error>(request, ct);

                if (response.Is(out Response<Error> errorResponse))
                {
                    failure = errorResponse.Message;
                    return null;
                }

                if (response.Is(out Response<PermissionsResponse> permissionResponse))
                {
                    return permissionResponse.Message;
                }

                failure = NotFound;
                return null;
            },
            CacheExpiration);

        if (permissionsResponse is not null)
        {
            return permissionsResponse;
        }

        return Result.Failure<PermissionsResponse>(failure ?? NotFound);
    }

    private static string CreateCacheKey(string identityId) => CacheKeys.Create("user_permissions", identityId);
}
