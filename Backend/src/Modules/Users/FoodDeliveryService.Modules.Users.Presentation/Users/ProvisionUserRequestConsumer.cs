using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Users.RegisterUser;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using MediatR;

namespace FoodDeliveryService.Modules.Users.Presentation.Users;

/// <summary>
/// Generalized synchronous entry point any service calls to provision an invited account for a given
/// role (mirrors <see cref="ProvisionManagerUserRequestConsumer"/> but the role is caller-supplied).
/// Validates the role against <see cref="Role.FromName"/> up front so an unknown or non-assignable
/// role surfaces to the caller as a clean Error response rather than a 500, then runs
/// RegisterUserCommand as an invited account and replies with the new UserId.
/// </summary>
public sealed class ProvisionUserRequestConsumer(ISender sender)
    : IConsumer<ProvisionUserRequest>
{
    public async Task Consume(ConsumeContext<ProvisionUserRequest> context)
    {
        var role = Role.FromName(context.Message.Role);

        if (role is null)
        {
            await context.RespondAsync(Error.Problem(
                "Users.RoleNotAssignable",
                $"The role '{context.Message.Role}' is unknown or cannot be assigned at provisioning."));

            return;
        }

        var command = new RegisterUserCommand(
            context.Message.Email,
            Password: string.Empty,
            context.Message.FirstName,
            context.Message.LastName,
            Role: role.Name,
            RequireInvitation: true);

        Result<Guid> result = await sender.Send(command);

        if (result.IsSuccess)
        {
            await context.RespondAsync(new ProvisionUserResponse(result.Value));
        }
        else
        {
            await context.RespondAsync(result.Error);
        }
    }
}
