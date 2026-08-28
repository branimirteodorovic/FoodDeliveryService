using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Support.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Create_Database : Migration
{
    // The agent queue's scan: open tickets, oldest first. A static field rather than an
    // inline array literal, because the analyzer set treats a repeated constant array
    // argument as an allocation worth naming (CA1861).
    private static readonly string[] TicketQueueScanColumns = ["status", "opened_on_utc"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "support_ticket_reference_seq");

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

        migrationBuilder.CreateTable(
            name: "tickets",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: true),
                subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                category = table.Column<int>(type: "integer", nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                source = table.Column<int>(type: "integer", nullable: false),
                escalation_transcript = table.Column<string>(type: "jsonb", nullable: true),
                assigned_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                opened_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                first_responded_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                resolved_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                closed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_tickets", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_inbox_messages_correlation_id",
            table: "inbox_messages",
            column: "correlation_id",
            filter: "correlation_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_inbox_messages_unprocessed",
            table: "inbox_messages",
            column: "occurred_on_utc",
            filter: "processed_on_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_correlation_id",
            table: "outbox_messages",
            column: "correlation_id",
            filter: "correlation_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_unprocessed",
            table: "outbox_messages",
            column: "occurred_on_utc",
            filter: "processed_on_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_tickets_assigned_agent_id",
            table: "tickets",
            column: "assigned_agent_id");

        migrationBuilder.CreateIndex(
            name: "ix_tickets_customer_id",
            table: "tickets",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_tickets_reference",
            table: "tickets",
            column: "reference",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_tickets_status_opened_on_utc",
            table: "tickets",
            columns: TicketQueueScanColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox_message_consumers");

        migrationBuilder.DropTable(
            name: "inbox_messages");

        migrationBuilder.DropTable(
            name: "outbox_message_consumers");

        migrationBuilder.DropTable(
            name: "outbox_messages");

        migrationBuilder.DropTable(
            name: "tickets");

        migrationBuilder.DropSequence(
            name: "support_ticket_reference_seq");
    }
}
