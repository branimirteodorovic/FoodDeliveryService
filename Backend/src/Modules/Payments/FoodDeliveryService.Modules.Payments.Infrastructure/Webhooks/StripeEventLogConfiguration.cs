using FoodDeliveryService.Modules.Payments.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Webhooks;

internal sealed class StripeEventLogConfiguration : IEntityTypeConfiguration<StripeEventLog>
{
    public void Configure(EntityTypeBuilder<StripeEventLog> builder)
    {
        builder.ToTable("stripe_event_logs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).ValueGeneratedNever();

        // Stripe's identifiers are documented as opaque and growable; generous bounds rather than
        // measurements of today's format, the same choice CustomerPaymentProfile makes.
        builder.Property(e => e.ProviderEventId).HasMaxLength(255);
        builder.Property(e => e.EventType).HasMaxLength(255);
        builder.Property(e => e.ObjectId).HasMaxLength(255);
        builder.Property(e => e.ObjectStatus).HasMaxLength(50);
        builder.Property(e => e.CustomerReference).HasMaxLength(255);
        builder.Property(e => e.PaymentMethodReference).HasMaxLength(255);

        // THE dedupe (§7.4). Stripe redelivers on any non-2xx and on its own schedule, and the read
        // in the handler cannot see a delivery that is in flight at the same instant — this index is
        // what makes that second one lose instead of being acted on twice.
        builder.HasIndex(e => e.ProviderEventId).IsUnique();

        // "Which events are still owed work?" — the operator's question, and the only one this table
        // is queried by. Partial, because the answer is a handful of rows out of everything Stripe
        // has ever sent.
        builder
            .HasIndex(e => e.ReceivedOnUtc)
            .HasFilter("processed_on_utc IS NULL");
    }
}
