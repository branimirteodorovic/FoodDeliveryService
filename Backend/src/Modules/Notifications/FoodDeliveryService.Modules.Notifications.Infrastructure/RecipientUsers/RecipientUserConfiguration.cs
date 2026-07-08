using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.RecipientUsers;

internal sealed class RecipientUserConfiguration : IEntityTypeConfiguration<RecipientUser>
{
    public void Configure(EntityTypeBuilder<RecipientUser> builder)
    {
        builder.ToTable("recipient_users");

        // Id IS the Users service's UserId (replica) — never generated locally.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Email).HasMaxLength(300);

        builder.Property(r => r.FirstName).HasMaxLength(200);

        builder.Property(r => r.LastName).HasMaxLength(200);
    }
}
