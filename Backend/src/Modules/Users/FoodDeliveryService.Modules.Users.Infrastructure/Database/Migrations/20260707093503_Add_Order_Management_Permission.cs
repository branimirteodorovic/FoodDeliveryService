using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FoodDeliveryService.Modules.Users.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Order_Management_Permission : Migration
{
    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0300:Simplify collection initialization", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "<Pending>")]
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "permissions",
            column: "code",
            value: "orders:manage");

        migrationBuilder.InsertData(
            table: "role_permissions",
            columns: new[] { "permission_code", "role_name" },
            values: new object[,]
            {
                { "orders:manage", "Administrator" },
                { "orders:manage", "RestaurantManager" }
            });
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0300:Simplify collection initialization", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "<Pending>")]
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "orders:manage", "Administrator" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "orders:manage", "RestaurantManager" });

        migrationBuilder.DeleteData(
            table: "permissions",
            keyColumn: "code",
            keyValue: "orders:manage");
    }
}
