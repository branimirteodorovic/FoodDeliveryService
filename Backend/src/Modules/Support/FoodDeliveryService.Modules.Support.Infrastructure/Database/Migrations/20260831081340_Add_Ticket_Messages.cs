using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Support.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Ticket_Messages : Migration
{
    // The only way this table is ever read: one ticket's thread in posting order. A static field
    // rather than an inline array literal, because the analyzer set treats a repeated constant array
    // argument as an allocation worth naming (CA1861).
    private static readonly string[] ThreadScanColumns = ["ticket_id", "posted_on_utc"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ticket_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                author_id = table.Column<Guid>(type: "uuid", nullable: false),
                author_kind = table.Column<int>(type: "integer", nullable: false),
                body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                visibility = table.Column<int>(type: "integer", nullable: false),
                posted_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ticket_messages", x => x.id);
                table.ForeignKey(
                    name: "fk_ticket_messages_tickets_ticket_id",
                    column: x => x.ticket_id,
                    principalTable: "tickets",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_ticket_messages_ticket_id_posted_on_utc",
            table: "ticket_messages",
            columns: ThreadScanColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ticket_messages");
    }
}
