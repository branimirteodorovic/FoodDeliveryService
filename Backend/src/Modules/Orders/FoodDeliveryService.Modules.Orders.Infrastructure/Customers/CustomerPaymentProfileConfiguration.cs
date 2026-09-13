using FoodDeliveryService.Modules.Orders.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Customers;

internal sealed class CustomerPaymentProfileConfiguration : IEntityTypeConfiguration<CustomerPaymentProfile>
{
    public void Configure(EntityTypeBuilder<CustomerPaymentProfile> builder)
    {
        builder.ToTable("customer_payment_profiles");

        builder.HasKey(p => p.Id);

        // Id IS the Users service's UserId, carried in on the event — never generated locally.
        builder.Property(p => p.Id).ValueGeneratedNever();
    }
}
