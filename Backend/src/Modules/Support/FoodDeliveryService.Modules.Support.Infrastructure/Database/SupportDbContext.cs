using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Domain.Agents;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Orders;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Database;

public sealed class SupportDbContext(DbContextOptions<SupportDbContext> options)
    : DbContext(options), IUnitOfWork
{
    /// <summary>
    /// The sequence behind <c>Ticket.Reference</c>. A sequence, not MAX()+1: two replicas reading
    /// the current maximum in the same instant would both write SUP-00000042.
    /// </summary>
    internal const string TicketReferenceSequence = "support_ticket_reference_seq";

    internal DbSet<Ticket> Tickets { get; set; }

    /// <summary>The people a ticket can be assigned to — support agents and administrators.</summary>
    internal DbSet<SupportAgentReplica> SupportAgents { get; set; }

    /// <summary>
    /// The append-only record of every agent action. Exposed as a DbSet because entries are written
    /// through the change tracker with the state change they describe; nothing reads it here — the
    /// audit endpoint goes through Dapper like every other read.
    /// </summary>
    internal DbSet<SupportAuditEntry> SupportAuditEntries { get; set; }

    /// <summary>
    /// Refund requests and the decisions on them. Its own aggregate, not a child of the ticket — a
    /// ticket can be resolved while a refund is still awaiting an administrator.
    /// </summary>
    internal DbSet<RefundRequest> RefundRequests { get; set; }

    /// <summary>
    /// The local order replica, projected from Orders' lifecycle events. Today it carries only what
    /// the refund ceiling needs; the status, timeline and driver fields land with the ticket-context
    /// milestone.
    /// </summary>
    internal DbSet<OrderSnapshot> OrderSnapshots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Outbox/inbox tables live in Common.Infrastructure, so the assembly scan below does not
        // find them — they stay explicitly applied.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConsumerConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());

        modelBuilder.HasSequence<long>(TicketReferenceSequence).StartsAt(1).IncrementsBy(1);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupportDbContext).Assembly);
    }
}
