using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure.Customers;

internal sealed class CustomerBehaviourConfiguration : IEntityTypeConfiguration<CustomerBehaviour>
{
    public void Configure(EntityTypeBuilder<CustomerBehaviour> builder)
    {
        builder.ToTable("customer_behaviours");

        // Id IS the Users service's UserId (projection) — never generated locally.
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // Total spend across every order the customer ever placed — matches the Orders module's
        // money precision so a value cannot be silently rounded on the way across the bus.
        builder.Property(c => c.TotalOrderValue).HasPrecision(10, 2);

        // The dashboard in Milestone E ranks by risk, but before that exists the triage screens read
        // "who has been active lately" off this ordering.
        builder.HasIndex(c => c.LastOrderOnUtc);
    }
}
