using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Agents.UpdateSupportAgent;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Support.Presentation.Agents;

/// <summary>
/// Keeps the agent replica's name in sync, so a renamed agent does not leave stale names scattered
/// across old audit entries and ticket lists. The command no-ops for users this module does not
/// replicate — which is nearly everybody — so every profile update on the platform is safe to
/// consume here.
/// </summary>
internal sealed class UserProfileUpdatedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserProfileUpdatedIntegrationEvent>
{
    public override async Task Handle(
        UserProfileUpdatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new UpdateSupportAgentCommand(
                integrationEvent.UserId,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpdateSupportAgentCommand),
                result.Error);
        }
    }
}
