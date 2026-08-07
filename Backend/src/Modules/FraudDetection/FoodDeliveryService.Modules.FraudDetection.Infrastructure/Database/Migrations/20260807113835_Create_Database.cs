using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Create_Database : Migration
{
    // Hoisted out of the CreateIndex call to satisfy CA1861 (no constant array arguments), which
    // this solution treats as an error.
    private static readonly string[] CustomerOrdersOverTime = ["customer_id", "placed_on_utc"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customer_behaviours",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                registered_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                first_seen_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                orders_placed = table.Column<int>(type: "integer", nullable: false),
                orders_cancelled = table.Column<int>(type: "integer", nullable: false),
                cancelled_before_pickup = table.Column<int>(type: "integer", nullable: false),
                orders_rejected = table.Column<int>(type: "integer", nullable: false),
                orders_delivered = table.Column<int>(type: "integer", nullable: false),
                total_order_value = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                last_order_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                window_started_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                orders_placed_in_window = table.Column<int>(type: "integer", nullable: false),
                orders_cancelled_in_window = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_customer_behaviours", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "driver_behaviours",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                first_seen_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                pickups_completed = table.Column<int>(type: "integer", nullable: false),
                deliveries_completed = table.Column<int>(type: "integer", nullable: false),
                offers_rejected = table.Column<int>(type: "integer", nullable: false),
                location_mismatches = table.Column<int>(type: "integer", nullable: false),
                last_delivery_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_driver_behaviours", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "inbox_message_consumers",
            columns: table => new
            {
                inbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inbox_message_consumers", x => new { x.inbox_message_id, x.name });
            });

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                content = table.Column<string>(type: "jsonb", maxLength: 2000, nullable: false),
                occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                error = table.Column<string>(type: "text", nullable: true),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                trace_parent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "order_facts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                subtotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                placed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                accepted_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ready_for_pickup_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                picked_up_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                delivered_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                cancelled_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                rejected_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                cancelled_before_pickup = table.Column<bool>(type: "boolean", nullable: false),
                times_unassigned = table.Column<int>(type: "integer", nullable: false),
                last_unassigned_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                delivery_id = table.Column<Guid>(type: "uuid", nullable: true),
                driver_id = table.Column<Guid>(type: "uuid", nullable: true),
                dropoff_latitude = table.Column<double>(type: "double precision", nullable: true),
                dropoff_longitude = table.Column<double>(type: "double precision", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_order_facts", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_message_consumers",
            columns: table => new
            {
                outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_message_consumers", x => new { x.outbox_message_id, x.name });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                content = table.Column<string>(type: "jsonb", maxLength: 2000, nullable: false),
                occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                error = table.Column<string>(type: "text", nullable: true),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                trace_parent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_customer_behaviours_last_order_on_utc",
            table: "customer_behaviours",
            column: "last_order_on_utc");

        migrationBuilder.CreateIndex(
            name: "ix_driver_behaviours_last_delivery_on_utc",
            table: "driver_behaviours",
            column: "last_delivery_on_utc");

        migrationBuilder.CreateIndex(
            name: "ix_inbox_messages_correlation_id",
            table: "inbox_messages",
            column: "correlation_id",
            filter: "correlation_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_order_facts_customer_id_placed_on_utc",
            table: "order_facts",
            columns: CustomerOrdersOverTime);

        migrationBuilder.CreateIndex(
            name: "ix_order_facts_delivery_id",
            table: "order_facts",
            column: "delivery_id");

        migrationBuilder.CreateIndex(
            name: "ix_order_facts_placed_on_utc",
            table: "order_facts",
            column: "placed_on_utc");

        migrationBuilder.CreateIndex(
            name: "ix_order_facts_restaurant_id",
            table: "order_facts",
            column: "restaurant_id");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_correlation_id",
            table: "outbox_messages",
            column: "correlation_id",
            filter: "correlation_id IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "customer_behaviours");

        migrationBuilder.DropTable(
            name: "driver_behaviours");

        migrationBuilder.DropTable(
            name: "inbox_message_consumers");

        migrationBuilder.DropTable(
            name: "inbox_messages");

        migrationBuilder.DropTable(
            name: "order_facts");

        migrationBuilder.DropTable(
            name: "outbox_message_consumers");

        migrationBuilder.DropTable(
            name: "outbox_messages");
    }
}
