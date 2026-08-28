using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Domain.Agents;

namespace FoodDeliveryService.Modules.Support.Application.Agents.UpsertSupportAgent;

internal sealed class UpsertSupportAgentCommandHandler(
    ISupportAgentRepository agentRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpsertSupportAgentCommand>
{
    public async Task<Result> Handle(UpsertSupportAgentCommand request, CancellationToken cancellationToken)
    {
        SupportAgentReplica? agent = await agentRepository.GetAsync(request.UserId, cancellationToken);

        if (agent is null)
        {
            agentRepository.Insert(
                SupportAgentReplica.Create(request.UserId, request.Email, request.FirstName, request.LastName));
        }
        else
        {
            agent.Update(request.FirstName, request.LastName);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
