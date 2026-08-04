using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Database.Migrations;
/// <inheritdoc />
public partial class Add_Message_Correlation_Columns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "correlation_id",
            table: "inbox_messages",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "trace_parent",
            table: "inbox_messages",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_inbox_messages_correlation_id",
            table: "inbox_messages",
            column: "correlation_id",
            filter: "correlation_id IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_inbox_messages_correlation_id",
            table: "inbox_messages");

        migrationBuilder.DropColumn(
            name: "correlation_id",
            table: "inbox_messages");

        migrationBuilder.DropColumn(
            name: "trace_parent",
            table: "inbox_messages");
    }
}
