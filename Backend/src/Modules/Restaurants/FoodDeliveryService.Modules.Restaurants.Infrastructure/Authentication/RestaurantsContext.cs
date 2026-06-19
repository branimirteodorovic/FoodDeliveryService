using FoodDeliveryService.Common.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Authentication;

internal sealed class RestaurantsContext(IHttpContextAccessor httpContextAccessor) : IRestaurantsContext
{
    public Guid NotificationId => httpContextAccessor.HttpContext?.User.GetUserId() ??
                              throw new Common.Application.Exceptions.ApplicationException("User identifier is unavailable");
}
