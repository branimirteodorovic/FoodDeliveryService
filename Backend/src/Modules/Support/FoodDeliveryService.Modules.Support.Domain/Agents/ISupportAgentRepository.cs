namespace FoodDeliveryService.Modules.Support.Domain.Agents;

public interface ISupportAgentRepository
{
    Task<SupportAgentReplica?> GetAsync(Guid agentId, CancellationToken cancellationToken = default);

    void Insert(SupportAgentReplica agent);
}
