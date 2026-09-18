using FoodDeliveryService.Modules.Payments.Domain.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Refunds;

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds");

        builder.HasKey(r => r.Id);

        // One approved request, one refund — and the database is what says so rather than the
        // handler's early return alone. Two deliveries of RefundApproved racing past each other
        // would otherwise both insert, and the customer would be paid twice.
        builder.HasIndex(r => r.RefundRequestId).IsUnique();

        // Not unique: an order may be refunded more than once, in parts. What bounds the total is
        // Payment.EnsureRefundable, which reads the settled sum through this index.
        builder.HasIndex(r => r.OrderId);

        builder.OwnsOne(r => r.Amount, amountBuilder =>
        {
            amountBuilder.Property(m => m.Amount).HasColumnName("amount").HasPrecision(10, 2);
            amountBuilder.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3);
        });

        // Stripe's identifiers are documented as opaque and growable; a generous bound, not a
        // measurement of today's format.
        builder.Property(r => r.StripeRefundId).HasMaxLength(255);

        // One of the bounded RefundFailureReason constants, the longest of which is 23 characters.
        builder.Property(r => r.FailureReason).HasMaxLength(50);

        // The same bound Support's own ticket reference carries. Copied in from the approval, never
        // read back (hard rule #5).
        builder.Property(r => r.TicketReference).HasMaxLength(20);
    }
}
