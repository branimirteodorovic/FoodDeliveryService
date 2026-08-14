using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Database.Migrations;

/// <summary>
/// Indexes the outbox/inbox dispatch predicate. Without it the Quartz jobs sequentially scan a
/// table that only ever grows — see <c>OutboxMessageConfiguration</c> for the measurement.
/// </summary>
public partial class Add_Message_Dispatch_Index : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_unprocessed",
            table: "outbox_messages",
            column: "occurred_on_utc",
            filter: "processed_on_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_inbox_messages_unprocessed",
            table: "inbox_messages",
            column: "occurred_on_utc",
            filter: "processed_on_utc IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_outbox_messages_unprocessed",
            table: "outbox_messages");

        migrationBuilder.DropIndex(
            name: "ix_inbox_messages_unprocessed",
            table: "inbox_messages");
    }
}
