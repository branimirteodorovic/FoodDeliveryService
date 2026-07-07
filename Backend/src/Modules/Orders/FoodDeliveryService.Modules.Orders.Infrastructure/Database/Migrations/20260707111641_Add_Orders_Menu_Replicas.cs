using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Orders_Menu_Replicas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "menu_items",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                is_available = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_menu_items", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "restaurants",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                manager_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                commission_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_restaurants", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_menu_items_restaurant_id",
            table: "menu_items",
            column: "restaurant_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "menu_items");

        migrationBuilder.DropTable(
            name: "restaurants");
    }
}
