using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Users.Application.Users.GetUserPermissions;

public sealed record GetUserPermissionsQuery(string IdentityId) : IQuery<PermissionsResponse>;
