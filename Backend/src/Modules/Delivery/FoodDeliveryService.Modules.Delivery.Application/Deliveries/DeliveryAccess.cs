using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries;

/// <summary>
/// Shared read-guard for a single delivery: only the order's customer, the assigned driver, or an
/// administrator may view it. Administrators are recognized by the admin-only
/// <see cref="Permissions.AdministerDeliveries"/> permission (the ownership bypass).
/// </summary>
internal static class DeliveryAccess
{
    internal static Result EnsureCanView(Guid customerId, Guid? driverId, IDeliveryContext context)
    {
        if (customerId == context.UserId ||
            driverId == context.UserId ||
            context.HasPermission(Permissions.AdministerDeliveries))
        {
            return Result.Success();
        }

        return Result.Failure(DeliveryErrors.NotAuthorizedToView);
    }
}
