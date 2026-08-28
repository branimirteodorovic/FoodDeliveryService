using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Domain.Agents;

namespace FoodDeliveryService.Modules.Support.Application.Agents.UpdateSupportAgent;

internal sealed class UpdateSupportAgentCommandHandler(
    ISupportAgentRepository agentRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateSupportAgentCommand>
{
    public async Task<Result> Handle(UpdateSupportAgentCommand request, CancellationToken cancellationToken)
    {
        SupportAgentReplica? agent = await agentRepository.GetAsync(request.UserId, cancellationToken);

        // Not a failure: a customer's profile update legitimately names nobody in this table, and
        // throwing here would make the inbox record an error for every non-agent in the platform.
        if (agent is null)
        {
            return Result.Success();
        }

        agent.Update(request.FirstName, request.LastName);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
