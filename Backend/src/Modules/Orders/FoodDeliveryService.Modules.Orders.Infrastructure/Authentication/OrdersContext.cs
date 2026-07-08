using FoodDeliveryService.Common.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Authentication;

internal sealed class OrdersContext(IHttpContextAccessor httpContextAccessor) : IOrdersContext
{
    public Guid UserId => httpContextAccessor.HttpContext?.User.GetUserId() ??
                              throw new Common.Application.Exceptions.ApplicationException("User identifier is unavailable");
}
