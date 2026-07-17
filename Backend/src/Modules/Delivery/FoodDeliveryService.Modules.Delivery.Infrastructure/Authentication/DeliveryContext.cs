using FoodDeliveryService.Common.Infrastructure.Authentication;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Authentication;

internal sealed class DeliveryContext(IHttpContextAccessor httpContextAccessor) : IDeliveryContext
{
    public Guid UserId => httpContextAccessor.HttpContext?.User.GetUserId() ??
                              throw new Common.Application.Exceptions.ApplicationException("User identifier is unavailable");

    // Permission claims are added per request by CustomClaimsTransformation (resolved from the Users
    // service, Redis-cached), so this is an in-memory check.
    public bool HasPermission(string permissionCode) =>
        httpContextAccessor.HttpContext?.User.GetPermissions().Contains(permissionCode) ?? false;
}
