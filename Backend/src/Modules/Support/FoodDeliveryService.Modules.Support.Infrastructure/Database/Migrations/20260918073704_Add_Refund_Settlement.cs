using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Support.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Refund_Settlement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_refund_requests_order_id",
            table: "refund_requests");

        migrationBuilder.AddColumn<DateTime>(
            name: "failed_on_utc",
            table: "refund_requests",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "failure_reason",
            table: "refund_requests",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "settled_on_utc",
            table: "refund_requests",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_refund_requests_order_id",
            table: "refund_requests",
            column: "order_id",
            unique: true,
            filter: "status IN (0, 1, 3)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_refund_requests_order_id",
            table: "refund_requests");

        migrationBuilder.DropColumn(
            name: "failed_on_utc",
            table: "refund_requests");

        migrationBuilder.DropColumn(
            name: "failure_reason",
            table: "refund_requests");

        migrationBuilder.DropColumn(
            name: "settled_on_utc",
            table: "refund_requests");

        migrationBuilder.CreateIndex(
            name: "ix_refund_requests_order_id",
            table: "refund_requests",
            column: "order_id",
            unique: true,
            filter: "status IN (0, 1)");
    }
}
