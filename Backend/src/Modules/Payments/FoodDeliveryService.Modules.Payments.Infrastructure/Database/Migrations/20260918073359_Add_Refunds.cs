using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Refunds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "refunded_on_utc",
            table: "payments",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "refunds",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                refund_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                ticket_reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                stripe_refund_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                failure_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                settled_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                failed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_refunds", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_refunds_order_id",
            table: "refunds",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "ix_refunds_refund_request_id",
            table: "refunds",
            column: "refund_request_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "refunds");

        migrationBuilder.DropColumn(
            name: "refunded_on_utc",
            table: "payments");
    }
}
