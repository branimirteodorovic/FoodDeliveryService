using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Orders_Customer_Payment_Profile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customer_payment_profiles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                can_pay_by_card = table.Column<bool>(type: "boolean", nullable: false),
                changed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_customer_payment_profiles", x => x.id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "customer_payment_profiles");
    }
}
