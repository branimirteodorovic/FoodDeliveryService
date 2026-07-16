using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FoodDeliveryService.Modules.Users.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Delivery_Role_And_Permissions : Migration
{
    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0300:Simplify collection initialization", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "<Pending>")]
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "permissions",
            column: "code",
            values: new object[]
            {
                "deliveries:administer",
                "deliveries:manage",
                "deliveries:read",
                "drivers:read",
                "drivers:update"
            });

        migrationBuilder.InsertData(
            table: "roles",
            column: "name",
            value: "DeliveryDriver");

        migrationBuilder.InsertData(
            table: "role_permissions",
            columns: new[] { "permission_code", "role_name" },
            values: new object[,]
            {
                { "deliveries:administer", "Administrator" },
                { "deliveries:manage", "Administrator" },
                { "deliveries:manage", "DeliveryDriver" },
                { "deliveries:read", "Administrator" },
                { "deliveries:read", "Customer" },
                { "deliveries:read", "DeliveryDriver" },
                { "drivers:read", "Administrator" },
                { "drivers:read", "DeliveryDriver" },
                { "drivers:update", "Administrator" },
                { "drivers:update", "DeliveryDriver" },
                { "users:read", "DeliveryDriver" },
                { "users:update", "DeliveryDriver" }
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
            keyValues: new object[] { "deliveries:administer", "Administrator" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "deliveries:manage", "Administrator" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "deliveries:manage", "DeliveryDriver" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "deliveries:read", "Administrator" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "deliveries:read", "Customer" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "deliveries:read", "DeliveryDriver" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "drivers:read", "Administrator" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "drivers:read", "DeliveryDriver" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "drivers:update", "Administrator" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "drivers:update", "DeliveryDriver" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "users:read", "DeliveryDriver" });

        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "users:update", "DeliveryDriver" });

        migrationBuilder.DeleteData(
            table: "permissions",
            keyColumn: "code",
            keyValue: "deliveries:administer");

        migrationBuilder.DeleteData(
            table: "permissions",
            keyColumn: "code",
            keyValue: "deliveries:manage");

        migrationBuilder.DeleteData(
            table: "permissions",
            keyColumn: "code",
            keyValue: "deliveries:read");

        migrationBuilder.DeleteData(
            table: "permissions",
            keyColumn: "code",
            keyValue: "drivers:read");

        migrationBuilder.DeleteData(
            table: "permissions",
            keyColumn: "code",
            keyValue: "drivers:update");

        migrationBuilder.DeleteData(
            table: "roles",
            keyColumn: "name",
            keyValue: "DeliveryDriver");
    }
}
