using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Database;

/// <summary>
/// The Payments module's unit of work over <c>fooddeliveryservice_payments</c>.
/// <para>
/// Its first migration created the outbox and inbox tables and nothing else, because the host
/// applies migrations at startup and a host whose database has no <c>outbox_messages</c> fails on
/// the first Quartz tick rather than at boot. <c>CustomerPaymentProfile</c> is the first aggregate
/// (Milestone D); <c>Payment</c>, <c>Refund</c> and <c>StripeEventLog</c> land in §7 and §8 the same
/// way, each with its own <c>IEntityTypeConfiguration</c> picked up by the assembly scan below.
/// </para>
/// </summary>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
    : DbContext(options), IUnitOfWork
{
    /// <summary>
    /// One row per registered user: their Stripe customer, and the one card they have saved. The
    /// rows are created from <c>UserRegisteredIntegrationEvent</c>, so the table is a superset of
    /// the customers who can actually pay by card.
    /// </summary>
    internal DbSet<CustomerPaymentProfile> CustomerPaymentProfiles { get; set; }

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
