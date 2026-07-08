using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Orders : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "commission_rate",
            table: "orders",
            type: "numeric(5,4)",
            precision: 5,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<Guid>(
            name: "customer_id",
            table: "orders",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<string>(
            name: "delivery_city",
            table: "orders",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "delivery_country",
            table: "orders",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "delivery_notes",
            table: "orders",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "delivery_postal_code",
            table: "orders",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "delivery_street",
            table: "orders",
            type: "character varying(300)",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "idempotency_key",
            table: "orders",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "payment_method",
            table: "orders",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "placed_on_utc",
            table: "orders",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.AddColumn<Guid>(
            name: "restaurant_id",
            table: "orders",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<int>(
            name: "status",
            table: "orders",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<decimal>(
            name: "subtotal",
            table: "orders",
            type: "numeric(10,2)",
            precision: 10,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.CreateTable(
            name: "order_items",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                menu_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                quantity = table.Column<int>(type: "integer", nullable: false),
                line_total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_order_items", x => x.id);
                table.ForeignKey(
                    name: "fk_order_items_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_orders_customer_id",
            table: "orders",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_orders_idempotency_key",
            table: "orders",
            column: "idempotency_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_orders_restaurant_id",
            table: "orders",
            column: "restaurant_id");

        migrationBuilder.CreateIndex(
            name: "ix_order_items_menu_item_id",
            table: "order_items",
            column: "menu_item_id");

        migrationBuilder.CreateIndex(
            name: "ix_order_items_order_id",
            table: "order_items",
            column: "order_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "order_items");

        migrationBuilder.DropIndex(
            name: "ix_orders_customer_id",
            table: "orders");

        migrationBuilder.DropIndex(
            name: "ix_orders_idempotency_key",
            table: "orders");

        migrationBuilder.DropIndex(
            name: "ix_orders_restaurant_id",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "commission_rate",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "customer_id",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "delivery_city",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "delivery_country",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "delivery_notes",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "delivery_postal_code",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "delivery_street",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "idempotency_key",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "payment_method",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "placed_on_utc",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "restaurant_id",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "status",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "subtotal",
            table: "orders");
    }
}
