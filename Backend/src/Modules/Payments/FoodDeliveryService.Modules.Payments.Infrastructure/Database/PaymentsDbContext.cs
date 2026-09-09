using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Database;

/// <summary>
/// The Payments module's unit of work over <c>fooddeliveryservice_payments</c>.
/// <para>
/// It carries no aggregate yet — this milestone ships the service, not its behaviour — so its first
/// migration creates the outbox and inbox tables and nothing else. That is deliberate rather than
/// premature: the host applies migrations at startup, and a host whose database has no
/// <c>outbox_messages</c> fails on the first Quartz tick rather than at boot.
/// <c>Payment</c>, <c>CustomerPaymentProfile</c>, <c>Refund</c> and <c>StripeEventLog</c> land in
/// §6 and §7 as <c>DbSet</c>s here, each with its own <c>IEntityTypeConfiguration</c> picked up by
/// the assembly scan below.
/// </para>
/// </summary>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
    : DbContext(options), IUnitOfWork
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Outbox/inbox tables live in Common.Infrastructure, so the assembly scan below does not
        // find them — they stay explicitly applied.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConsumerConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }
}
