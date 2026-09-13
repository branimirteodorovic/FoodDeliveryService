using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Customer_Payment_Profiles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customer_payment_profiles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                stripe_customer_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                payment_method_id = table.Column<Guid>(type: "uuid", nullable: true),
                stripe_payment_method_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                brand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                expiry_month = table.Column<int>(type: "integer", nullable: true),
                expiry_year = table.Column<int>(type: "integer", nullable: true),
                attached_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_customer_payment_profiles", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_customer_payment_profiles_payment_method_id",
            table: "customer_payment_profiles",
            column: "payment_method_id",
            unique: true,
            filter: "payment_method_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_customer_payment_profiles_stripe_customer_id",
            table: "customer_payment_profiles",
            column: "stripe_customer_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "customer_payment_profiles");
    }
}
