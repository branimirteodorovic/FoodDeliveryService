using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Authentication;
using FoodDeliveryService.Common.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Authentication;

internal sealed class NotificationsContext(IHttpContextAccessor httpContextAccessor) : INotificationContext
{
    public Guid NotificationId => httpContextAccessor.HttpContext?.User.GetUserId() ??
                              throw new Common.Application.Exceptions.ApplicationException("User identifier is unavailable");
}
