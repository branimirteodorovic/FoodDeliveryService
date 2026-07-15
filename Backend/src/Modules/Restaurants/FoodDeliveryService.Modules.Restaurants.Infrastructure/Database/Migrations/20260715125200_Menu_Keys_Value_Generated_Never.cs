using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Menu_Keys_Value_Generated_Never : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty. MenuCategory.Id and MenuItem.Id switched from store-generated to
        // ValueGeneratedNever so EF inserts (not updates) graph-discovered menu children whose ids
        // the domain already assigns. That is a value-generation strategy change only — the columns
        // stay identical uuid primary keys — so there is no DDL; this migration just re-syncs the
        // model snapshot.
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty — the Up direction makes no schema change to revert.
    }
}
