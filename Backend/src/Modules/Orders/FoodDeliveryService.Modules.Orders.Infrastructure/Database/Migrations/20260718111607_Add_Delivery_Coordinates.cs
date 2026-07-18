using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Delivery_Coordinates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "latitude",
            table: "restaurants",
            type: "double precision",
            nullable: false,
            defaultValue: 0.0);

        migrationBuilder.AddColumn<double>(
            name: "longitude",
            table: "restaurants",
            type: "double precision",
            nullable: false,
            defaultValue: 0.0);

        migrationBuilder.AddColumn<double>(
            name: "delivery_latitude",
            table: "orders",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "delivery_longitude",
            table: "orders",
            type: "double precision",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "latitude",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "longitude",
            table: "restaurants");

        migrationBuilder.DropColumn(
            name: "delivery_latitude",
            table: "orders");

        migrationBuilder.DropColumn(
            name: "delivery_longitude",
            table: "orders");
    }
}
