using FoodDeliveryService.Modules.Support.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Agents;

internal sealed class SupportAgentConfiguration : IEntityTypeConfiguration<SupportAgentReplica>
{
    public void Configure(EntityTypeBuilder<SupportAgentReplica> builder)
    {
        // "support_agents" rather than the type's own name: the Replica suffix says how the row got
        // here, which is a fact about the projection and not about what the table holds.
        builder.ToTable("support_agents");

        builder.HasKey(a => a.Id);

        // The key is the Users service's UserId, carried in on the event — never generated here.
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Email).HasMaxLength(300);
        builder.Property(a => a.FirstName).HasMaxLength(200);
        builder.Property(a => a.LastName).HasMaxLength(200);
    }
}
