using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Deliveries : Migration
{
    private static readonly string[] ExpiredOffersScanColumns = ["status", "offer_expires_on_utc"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "deliveries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                pickup_latitude = table.Column<double>(type: "double precision", nullable: false),
                pickup_longitude = table.Column<double>(type: "double precision", nullable: false),
                dropoff_street = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                dropoff_city = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                dropoff_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                dropoff_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                dropoff_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                dropoff_latitude = table.Column<double>(type: "double precision", nullable: false),
                dropoff_longitude = table.Column<double>(type: "double precision", nullable: false),
                driver_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<int>(type: "integer", nullable: false),
                offered_driver_id = table.Column<Guid>(type: "uuid", nullable: true),
                offer_expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                assigned_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                picked_up_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                delivered_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                tried_driver_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_deliveries", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_driver_id",
            table: "deliveries",
            column: "driver_id");

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_order_id",
            table: "deliveries",
            column: "order_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_status_offer_expires_on_utc",
            table: "deliveries",
            columns: ExpiredOffersScanColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "deliveries");
    }
}
