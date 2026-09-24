using FoodDeliveryService.Common.Infrastructure.Authentication;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Modules.Users.Infrastructure.Authentication;

internal sealed class UsersContext(IHttpContextAccessor httpContextAccessor) : IUsersContext
{
    public Guid UserId => httpContextAccessor.HttpContext?.User.GetUserId() ??
        throw new Common.Application.Exceptions.ApplicationException("User identifier is unavailable");
}
