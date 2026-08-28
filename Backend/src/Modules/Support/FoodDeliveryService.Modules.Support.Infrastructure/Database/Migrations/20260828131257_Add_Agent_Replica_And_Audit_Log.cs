using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Support.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Agent_Replica_And_Audit_Log : Migration
{
    // The only way the audit table is ever read: one ticket's history, newest first. A static field
    // rather than an inline array literal, because the analyzer set treats a repeated constant array
    // argument as an allocation worth naming (CA1861).
    private static readonly string[] TicketHistoryScanColumns = ["ticket_id", "occurred_on_utc"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "support_agents",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                first_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                last_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_support_agents", x => x.id);
            });

        // No foreign key to tickets, deliberately: a cascade is the one thing that could ever delete
        // an audit row, and an append-only log must have no delete path at all.
        migrationBuilder.CreateTable(
            name: "support_audit_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                action = table.Column<int>(type: "integer", nullable: false),
                from_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                to_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_support_audit_entries", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_support_audit_entries_ticket_id_occurred_on_utc",
            table: "support_audit_entries",
            columns: TicketHistoryScanColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "support_agents");

        migrationBuilder.DropTable(
            name: "support_audit_entries");
    }
}
