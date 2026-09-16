using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Orders_Payment_Status : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // defaultValue is hand-edited from the generated 0 to 1 — PaymentStatus.NotRequired.
        // Zero is not a member of the enum, so every order that existed before this column did
        // would have read back as an unnamed value. One is also the correct answer for them on
        // the merits: PaymentMethod.Card did not exist until this milestone, so every row in
        // this table is a cash order with no payment to track.
        migrationBuilder.AddColumn<int>(
            name: "payment_status",
            table: "orders",
            type: "integer",
            nullable: false,
            defaultValue: 1);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "payment_status",
            table: "orders");
    }
}
