using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Users.Application.Users.GetUser;

public sealed record GetUserQuery(Guid UserId) : IQuery<UserResponse>;
