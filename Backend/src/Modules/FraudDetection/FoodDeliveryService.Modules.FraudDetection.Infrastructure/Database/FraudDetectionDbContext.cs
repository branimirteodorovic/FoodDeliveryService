using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure.Database;

/// <summary>
/// FraudDetection's own database (fooddeliveryservice_frauddetection). Every table in it is derived state: the three
/// behavioural projections plus the outbox/inbox. Nothing here is a system of record for anything
/// another service owns — hard rule #5 in its purest form, since FraudDetection reads exclusively from the
/// bus.
/// </summary>
public sealed class FraudDetectionDbContext(DbContextOptions<FraudDetectionDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<CustomerBehaviour> CustomerBehaviours { get; set; }

    internal DbSet<DriverBehaviour> DriverBehaviours { get; set; }

    internal DbSet<OrderFact> OrderFacts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Outbox/inbox tables live in Common.Infrastructure, so the assembly scan below does not
        // find them — they stay explicitly applied. FraudDetection raises no domain events in Milestone A,
        // but the outbox exists from the start: the alert aggregate in Milestone C publishes
        // FraudAlertRaised through it, and a service without an outbox is a service that cannot.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConsumerConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FraudDetectionDbContext).Assembly);
    }
}
