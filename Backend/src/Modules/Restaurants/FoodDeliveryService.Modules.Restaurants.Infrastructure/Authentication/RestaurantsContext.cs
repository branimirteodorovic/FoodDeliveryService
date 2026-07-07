using FoodDeliveryService.Common.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Authentication;

internal sealed class RestaurantsContext(IHttpContextAccessor httpContextAccessor) : IRestaurantsContext
{
    public Guid UserId => httpContextAccessor.HttpContext?.User.GetUserId() ??
                          throw new Common.Application.Exceptions.ApplicationException("User identifier is unavailable");

    // Permission claims are added per request by CustomClaimsTransformation (resolved from the
    // Users service, Redis-cached), so this is an in-memory check.
    public bool HasPermission(string permissionCode) =>
        httpContextAccessor.HttpContext?.User.GetPermissions().Contains(permissionCode) ?? false;
}
