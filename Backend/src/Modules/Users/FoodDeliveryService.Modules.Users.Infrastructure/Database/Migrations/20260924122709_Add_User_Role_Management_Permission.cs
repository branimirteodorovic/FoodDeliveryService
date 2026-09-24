using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Users.Infrastructure.Database.Migrations;

/// <summary>
/// Seeds <c>user-roles:manage</c> and grants it to <c>Administrator</c> alone — the permission
/// behind <c>PUT users/{userId}/roles</c>, the first and only way a role changes after an account
/// is created.
/// <para>
/// A dedicated code rather than a widening of <c>users:provision</c>: that one creates accounts
/// whose role is chosen once, while this one re-grants and revokes privileges on an account that
/// already exists and may already be signed in. No other role receives it — changing what someone
/// else may do is not part of any operational job on this platform.
/// </para>
/// </summary>
public partial class Add_User_Role_Management_Permission : Migration
{
    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "Generated migration seed data; each array is used exactly once.")]
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "permissions",
            column: "code",
            value: "user-roles:manage");

        migrationBuilder.InsertData(
            table: "role_permissions",
            columns: new[] { "permission_code", "role_name" },
            values: new object[] { "user-roles:manage", "Administrator" });
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "Generated migration seed data; each array is used exactly once.")]
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "role_permissions",
            keyColumns: new[] { "permission_code", "role_name" },
            keyValues: new object[] { "user-roles:manage", "Administrator" });

        migrationBuilder.DeleteData(
            table: "permissions",
            keyColumn: "code",
            keyValue: "user-roles:manage");
    }
}
