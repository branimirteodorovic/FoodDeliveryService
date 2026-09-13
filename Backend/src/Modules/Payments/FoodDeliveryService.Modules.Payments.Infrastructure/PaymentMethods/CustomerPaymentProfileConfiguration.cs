using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.PaymentMethods;

internal sealed class CustomerPaymentProfileConfiguration : IEntityTypeConfiguration<CustomerPaymentProfile>
{
    public void Configure(EntityTypeBuilder<CustomerPaymentProfile> builder)
    {
        builder.ToTable("customer_payment_profiles");

        builder.HasKey(p => p.Id);

        // The key IS the Users service's UserId, carried in on UserRegistered — never generated here.
        builder.Property(p => p.Id).ValueGeneratedNever();

        // Stripe's identifiers are documented as opaque and growable; these are generous bounds, not
        // measurements of today's format.
        builder.Property(p => p.StripeCustomerId).HasMaxLength(255);
        builder.Property(p => p.StripePaymentMethodId).HasMaxLength(255);

        // Display only, and sized to say so. Last4 is char-ish rather than numeric on purpose: it is
        // a label that can start with a zero, not a number anything computes with.
        builder.Property(p => p.Brand).HasMaxLength(50);
        builder.Property(p => p.Last4).HasMaxLength(4);

        // One profile per Stripe customer, enforced by the database rather than by the handler's
        // early return alone: two registration events for one user racing through two replicas would
        // otherwise create two rows pointing at the same cus_….
        builder.HasIndex(p => p.StripeCustomerId).IsUnique();

        // The card's public identifier is looked up by the DELETE path and must be unique across the
        // table, not just within a row — a filtered index because it is null for every customer who
        // has not saved a card, which is most of them.
        builder
            .HasIndex(p => p.PaymentMethodId)
            .IsUnique()
            .HasFilter("payment_method_id IS NOT NULL");
    }
}
