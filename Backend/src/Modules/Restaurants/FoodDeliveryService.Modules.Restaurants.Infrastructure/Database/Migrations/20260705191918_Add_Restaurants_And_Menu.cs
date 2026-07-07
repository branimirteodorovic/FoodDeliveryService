using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Restaurants_And_Menu : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "address_city",
            table: "restaurants",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "address_country",
            table: "restaurants",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<double>(
            name: "address_latitude",
            table: "restaurants",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "address_longitude",
            table: "restaurants",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "address_postal_code",
            table: "restaurants",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "address_street",
            table: "restaurants",
            type: "character varying(300)",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<decimal>(
            name: "commission_rate",
            table: "restaurants",
            type: "numeric(5,4)",
            precision: 5,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<DateTime>(
            name: "created_on_utc",
            table: "restaurants",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.AddColumn<string>(
            name: "cuisine_type",
            table: "restaurants",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "email",
            table: "restaurants",
            type: "character varying(300)",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<Guid>(
            name: "manager_user_id",
            table: "restaurants",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<string>(
            name: "name",
            table: "restaurants",
            type: "character varying(300)",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "phone_number",
            table: "restaurants",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "status",
            table: "restaurants",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "tax_identification",
            table: "restaurants",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateTable(
            name: "menu_categories",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                display_order = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_menu_categories", x => x.id);
                table.ForeignKey(
                    name: "fk_menu_categories_restaurants_restaurant_id",
                    column: x => x.restaurant_id,
                    principalTable: "restaurants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "restaurant_managers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                first_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                last_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_restaurant_managers", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "menu_items",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                category_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                photo_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                is_available = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_menu_items", x => x.id);
                table.ForeignKey(
                    name: "fk_menu_items_menu_categories_category_id",
                    column: x => x.category_id,
                    principalTable: "menu_categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_restaurants_manager_user_id",
            table: "restaurants",
            column: "manager_user_id");

        migrationBuilder.CreateIndex(
            name: "ix_menu_categories_restaurant_id",
            table: "menu_categories",
            column: "restaurant_id");

        migrationBuilder.CreateIndex(
            name: "ix_menu_items_category_id",
            table: "menu_items",
            column: "category_id");

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
            name: "restaurant_managers");

        migrationBuilder.DropTable(
            name: "menu_categories");

        migrationBuilder.DropIndex(
            name: "ix_restaurants_manager_user_id",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "address_city",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "address_country",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "address_latitude",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "address_longitude",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "address_postal_code",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "address_street",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "commission_rate",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "created_on_utc",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "cuisine_type",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "email",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "manager_user_id",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "name",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "phone_number",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "status",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "tax_identification",
            table: "restaurants");
    }
}
