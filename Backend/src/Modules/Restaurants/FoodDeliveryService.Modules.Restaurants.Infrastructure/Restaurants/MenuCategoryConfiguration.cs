using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Restaurants;

internal sealed class MenuCategoryConfiguration : IEntityTypeConfiguration<MenuCategory>
{
    public void Configure(EntityTypeBuilder<MenuCategory> builder)
    {
        builder.ToTable("menu_categories");

        builder.HasKey(c => c.Id);

        // The domain assigns the id in MenuCategory.Create. Left as store-generated (the Guid
        // convention), EF assumes a graph-discovered category with its key already set is an
        // existing row and marks it Modified — issuing an UPDATE that matches nothing instead of
        // an INSERT. The aggregate root is spared only because its repository calls Add explicitly.
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).HasMaxLength(200);

        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
