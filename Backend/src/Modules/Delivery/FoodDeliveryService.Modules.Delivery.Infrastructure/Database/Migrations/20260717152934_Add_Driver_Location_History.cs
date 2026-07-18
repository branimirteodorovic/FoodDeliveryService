using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Driver_Location_History : Migration
{
    private static readonly string[] DriverIdRecordedOnUtcColumns = ["driver_id", "recorded_on_utc"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "driver_location_history",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                latitude = table.Column<double>(type: "double precision", nullable: false),
                longitude = table.Column<double>(type: "double precision", nullable: false),
                recorded_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                delivery_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_driver_location_history", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_driver_location_history_driver_id_recorded_on_utc",
            table: "driver_location_history",
            columns: DriverIdRecordedOnUtcColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "driver_location_history");
    }
}
