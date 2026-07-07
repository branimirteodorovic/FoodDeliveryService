using FoodDeliveryService.Modules.Orders.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Customers;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        // Id IS the Users service's UserId (replica) — never generated locally.
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Email).HasMaxLength(300);

        builder.Property(c => c.FirstName).HasMaxLength(200);

        builder.Property(c => c.LastName).HasMaxLength(200);
    }
}
