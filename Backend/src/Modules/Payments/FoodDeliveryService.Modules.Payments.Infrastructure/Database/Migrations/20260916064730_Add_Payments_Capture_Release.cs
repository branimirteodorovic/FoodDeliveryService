using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Payments_Capture_Release : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "captured_on_utc",
            table: "payments",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "released_on_utc",
            table: "payments",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "captured_on_utc",
            table: "payments");

        migrationBuilder.DropColumn(
            name: "released_on_utc",
            table: "payments");
    }
}
