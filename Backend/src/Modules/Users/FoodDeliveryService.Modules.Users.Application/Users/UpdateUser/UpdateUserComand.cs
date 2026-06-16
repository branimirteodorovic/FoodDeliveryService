using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Users.Application.Users.UpdateUser;

public sealed record UpdateUserCommand(Guid UserId, string FirstName, string LastName) : ICommand;
