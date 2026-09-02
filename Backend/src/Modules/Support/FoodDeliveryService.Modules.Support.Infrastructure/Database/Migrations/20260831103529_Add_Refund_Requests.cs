using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Support.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Refund_Requests : Migration
{
    // The at-most-one-live-refund-per-order rule, as the database states it. The command handler's
    // pre-check produces the clean 409; this partial unique index is what holds when two agents on
    // two tickets for the same order pass that check in the same instant, because no aggregate in
    // this codebase carries an optimistic concurrency token. Rejected (2) is excluded on purpose —
    // a refused request must not lock the order out of a better-argued second attempt.

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "order_snapshots",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                placed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_event_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_order_snapshots", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "refund_requests",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                ticket_reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                requested_by_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                decided_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                decision_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                requested_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                decided_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_refund_requests", x => x.id);
                table.ForeignKey(
                    name: "fk_refund_requests_tickets_ticket_id",
                    column: x => x.ticket_id,
                    principalTable: "tickets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_order_snapshots_customer_id",
            table: "order_snapshots",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_refund_requests_order_id",
            table: "refund_requests",
            column: "order_id",
            unique: true,
            filter: "status IN (0, 1)");

        migrationBuilder.CreateIndex(
            name: "ix_refund_requests_status",
            table: "refund_requests",
            column: "status");

        migrationBuilder.CreateIndex(
            name: "ix_refund_requests_ticket_id",
            table: "refund_requests",
            column: "ticket_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "order_snapshots");

        migrationBuilder.DropTable(
            name: "refund_requests");
    }
}
