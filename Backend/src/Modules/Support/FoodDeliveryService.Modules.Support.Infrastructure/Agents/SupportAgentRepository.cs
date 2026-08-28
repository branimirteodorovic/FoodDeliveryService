using FoodDeliveryService.Modules.Support.Domain.Agents;
using FoodDeliveryService.Modules.Support.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Agents;

internal sealed class SupportAgentRepository(SupportDbContext context) : ISupportAgentRepository
{
    public async Task<SupportAgentReplica?> GetAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        return await context.SupportAgents.SingleOrDefaultAsync(a => a.Id == agentId, cancellationToken);
    }

    public void Insert(SupportAgentReplica agent)
    {
        context.SupportAgents.Add(agent);
    }
}
