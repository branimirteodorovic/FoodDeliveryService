using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FoodDeliveryService.Modules.Users.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Restaurant_Roles_And_Permissions : Migration
{
        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0300:Simplify collection initialization", Justification = "<Pending>")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "<Pending>")]
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "carts:add", "Member" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "carts:read", "Member" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "carts:remove", "Member" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "events:search", "Member" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "orders:create", "Member" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "orders:read", "Member" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "ticket-types:read", "Member" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "tickets:check-in", "Member" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "tickets:read", "Member" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "users:read", "Member" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "users:update", "Member" });

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "name",
                keyValue: "Member");

            migrationBuilder.InsertData(
                table: "permissions",
                column: "code",
                values: new object[]
                {
                    "menu:manage",
                    "menu:read",
                    "restaurants:create",
                    "restaurants:read",
                    "restaurants:update",
                    "users:provision"
                });

            migrationBuilder.InsertData(
                table: "roles",
                column: "name",
                values: new object[]
                {
                    "Customer",
                    "RestaurantManager"
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_name" },
                values: new object[,]
                {
                    { "carts:add", "Customer" },
                    { "carts:read", "Customer" },
                    { "carts:remove", "Customer" },
                    { "events:search", "Customer" },
                    { "menu:manage", "Administrator" },
                    { "menu:manage", "RestaurantManager" },
                    { "menu:read", "Administrator" },
                    { "menu:read", "Customer" },
                    { "menu:read", "RestaurantManager" },
                    { "orders:create", "Customer" },
                    { "orders:read", "Customer" },
                    { "restaurants:create", "Administrator" },
                    { "restaurants:read", "Administrator" },
                    { "restaurants:read", "Customer" },
                    { "restaurants:read", "RestaurantManager" },
                    { "restaurants:update", "Administrator" },
                    { "restaurants:update", "RestaurantManager" },
                    { "ticket-types:read", "Customer" },
                    { "tickets:check-in", "Customer" },
                    { "tickets:read", "Customer" },
                    { "users:provision", "Administrator" },
                    { "users:read", "Customer" },
                    { "users:read", "RestaurantManager" },
                    { "users:update", "Customer" },
                    { "users:update", "RestaurantManager" }
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
                keyValues: new object[] { "carts:add", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "carts:read", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "carts:remove", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "events:search", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "menu:manage", "Administrator" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "menu:manage", "RestaurantManager" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "menu:read", "Administrator" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "menu:read", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "menu:read", "RestaurantManager" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "orders:create", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "orders:read", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "restaurants:create", "Administrator" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "restaurants:read", "Administrator" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "restaurants:read", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "restaurants:read", "RestaurantManager" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "restaurants:update", "Administrator" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "restaurants:update", "RestaurantManager" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "ticket-types:read", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "tickets:check-in", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "tickets:read", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "users:provision", "Administrator" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "users:read", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "users:read", "RestaurantManager" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "users:update", "Customer" });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "permission_code", "role_name" },
                keyValues: new object[] { "users:update", "RestaurantManager" });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "menu:manage");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "menu:read");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "restaurants:create");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "restaurants:read");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "restaurants:update");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "code",
                keyValue: "users:provision");

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "name",
                keyValue: "Customer");

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "name",
                keyValue: "RestaurantManager");

            migrationBuilder.InsertData(
                table: "roles",
                column: "name",
                value: "Member");

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_name" },
                values: new object[,]
                {
                    { "carts:add", "Member" },
                    { "carts:read", "Member" },
                    { "carts:remove", "Member" },
                    { "events:search", "Member" },
                    { "orders:create", "Member" },
                    { "orders:read", "Member" },
                    { "ticket-types:read", "Member" },
                    { "tickets:check-in", "Member" },
                    { "tickets:read", "Member" },
                    { "users:read", "Member" },
                    { "users:update", "Member" }
                });
        }
    }
