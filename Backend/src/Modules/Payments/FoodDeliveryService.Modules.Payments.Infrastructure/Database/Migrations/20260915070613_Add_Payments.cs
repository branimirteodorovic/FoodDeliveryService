using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Payments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "failure_reason",
            table: "stripe_event_logs",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "order_reference",
            table: "stripe_event_logs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "payments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                stripe_payment_intent_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                failure_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                authorized_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                failed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_payments", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_payments_customer_id",
            table: "payments",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_payments_order_id",
            table: "payments",
            column: "order_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_payments_stripe_payment_intent_id",
            table: "payments",
            column: "stripe_payment_intent_id",
            unique: true,
            filter: "stripe_payment_intent_id IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "payments");

        migrationBuilder.DropColumn(
            name: "failure_reason",
            table: "stripe_event_logs");

        migrationBuilder.DropColumn(
            name: "order_reference",
            table: "stripe_event_logs");
    }
}
