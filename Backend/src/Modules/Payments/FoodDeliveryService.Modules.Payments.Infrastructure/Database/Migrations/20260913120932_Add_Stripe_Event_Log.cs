using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Stripe_Event_Log : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "stripe_event_logs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                provider_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                event_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                object_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                object_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                customer_reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                payment_method_reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                received_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stripe_event_logs", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_stripe_event_logs_provider_event_id",
            table: "stripe_event_logs",
            column: "provider_event_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_stripe_event_logs_received_on_utc",
            table: "stripe_event_logs",
            column: "received_on_utc",
            filter: "processed_on_utc IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "stripe_event_logs");
    }
}
