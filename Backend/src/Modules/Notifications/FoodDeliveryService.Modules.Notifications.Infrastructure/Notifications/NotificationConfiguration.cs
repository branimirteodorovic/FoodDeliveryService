using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Notifications;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.RecipientEmail).HasMaxLength(300).IsRequired();

        builder.Property(n => n.Subject).HasMaxLength(500).IsRequired();

        // Persist the enums as text — this is an audit log, so readability beats compactness.
        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(n => n.Channel).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.HasIndex(n => n.RecipientUserId);
    }
}
