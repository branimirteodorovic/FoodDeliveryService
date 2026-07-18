using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Delivery_Replicas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "orders",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                delivery_street = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                delivery_city = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                delivery_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                delivery_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                delivery_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                delivery_latitude = table.Column<double>(type: "double precision", nullable: false),
                delivery_longitude = table.Column<double>(type: "double precision", nullable: false),
                placed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_orders", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "restaurants",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                latitude = table.Column<double>(type: "double precision", nullable: false),
                longitude = table.Column<double>(type: "double precision", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_restaurants", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_orders_customer_id",
            table: "orders",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_orders_restaurant_id",
            table: "orders",
            column: "restaurant_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "orders");

        migrationBuilder.DropTable(
            name: "restaurants");
    }
}
