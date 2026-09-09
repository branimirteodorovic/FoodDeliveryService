using FoodDeliveryService.Common.Infrastructure.Authentication;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Authentication;

internal sealed class PaymentsContext(IHttpContextAccessor httpContextAccessor) : IPaymentsContext
{
    public Guid UserId => httpContextAccessor.HttpContext?.User.GetUserId() ??
        throw new Common.Application.Exceptions.ApplicationException("User identifier is unavailable");

    // Permission claims are added per request by CustomClaimsTransformation (resolved from the
    // Users service over the bus, Redis-cached), so this is an in-memory check.
    public bool HasPermission(string permissionCode) =>
        httpContextAccessor.HttpContext?.User.GetPermissions().Contains(permissionCode) ?? false;
}
