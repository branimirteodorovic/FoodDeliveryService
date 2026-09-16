using FoodDeliveryService.Modules.Payments.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Payments;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);

        // One order, one payment — and the database is what says so rather than the handler's early
        // return alone. Two deliveries of OrderPlaced racing past each other would otherwise both
        // insert, and the second authorization would put a second hold on the customer's card.
        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.HasIndex(p => p.CustomerId);

        // Money is owned rather than two loose columns, so the amount and the currency it is
        // denominated in cannot be read apart. Column names are spelled out because the convention
        // would otherwise prefix them with the property name.
        builder.OwnsOne(p => p.Amount, amountBuilder =>
        {
            amountBuilder.Property(m => m.Amount).HasColumnName("amount").HasPrecision(10, 2);
            amountBuilder.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3);
        });

        // Stripe's identifiers are documented as opaque and growable; a generous bound, not a
        // measurement of today's format. Unique because two payments sharing one intent would mean
        // two orders believing they own the same hold — and it is the index the webhook side's
        // order-id lookup rides on.
        builder.Property(p => p.StripePaymentIntentId).HasMaxLength(255);

        builder
            .HasIndex(p => p.StripePaymentIntentId)
            .IsUnique()
            .HasFilter("stripe_payment_intent_id IS NOT NULL");

        // One of the bounded PaymentFailureReason constants, the longest of which is 22 characters.
        builder.Property(p => p.FailureReason).HasMaxLength(50);
    }
}
