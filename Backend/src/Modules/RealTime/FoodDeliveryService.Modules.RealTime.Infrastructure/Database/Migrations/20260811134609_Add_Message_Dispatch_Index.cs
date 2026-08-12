using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Database.Migrations;

/// <summary>
/// Indexes the inbox dispatch predicate — RealTime consumes and never publishes, so it has no
/// outbox to index. See <c>InboxMessageConfiguration</c> for the measurement.
/// </summary>
public partial class Add_Message_Dispatch_Index : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
            name: "ix_inbox_messages_unprocessed",
            table: "inbox_messages");
    }
}
