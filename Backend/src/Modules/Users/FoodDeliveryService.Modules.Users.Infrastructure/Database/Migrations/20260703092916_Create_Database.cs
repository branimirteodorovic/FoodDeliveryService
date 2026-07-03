using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FoodDeliveryService.Modules.Users.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Create_Database : Migration
{
    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0300:Simplify collection initialization", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "<Pending>")]
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "inbox_message_consumers",
            columns: table => new
            {
                inbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inbox_message_consumers", x => new { x.inbox_message_id, x.name });
            });

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                content = table.Column<string>(type: "jsonb", maxLength: 2000, nullable: false),
                occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_message_consumers",
            columns: table => new
            {
                outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_message_consumers", x => new { x.outbox_message_id, x.name });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                content = table.Column<string>(type: "jsonb", maxLength: 2000, nullable: false),
                occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "permissions",
            columns: table => new
            {
                code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_permissions", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "roles",
            columns: table => new
            {
                name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_roles", x => x.name);
            });

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                first_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                last_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                identity_id = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "role_permissions",
            columns: table => new
            {
                permission_code = table.Column<string>(type: "character varying(100)", nullable: false),
                role_name = table.Column<string>(type: "character varying(50)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_role_permissions", x => new { x.permission_code, x.role_name });
                table.ForeignKey(
                    name: "fk_role_permissions_permissions_permission_code",
                    column: x => x.permission_code,
                    principalTable: "permissions",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_role_permissions_roles_role_name",
                    column: x => x.role_name,
                    principalTable: "roles",
                    principalColumn: "name",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_roles",
            columns: table => new
            {
                role_name = table.Column<string>(type: "character varying(50)", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_roles", x => new { x.role_name, x.user_id });
                table.ForeignKey(
                    name: "fk_user_roles_roles_roles_name",
                    column: x => x.role_name,
                    principalTable: "roles",
                    principalColumn: "name",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_user_roles_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            table: "permissions",
            column: "code",
            values: new object[]
            {
                "carts:add",
                "carts:read",
                "carts:remove",
                "categories:read",
                "categories:update",
                "event-statistics:read",
                "events:read",
                "events:search",
                "events:update",
                "orders:create",
                "orders:read",
                "ticket-types:read",
                "ticket-types:update",
                "tickets:check-in",
                "tickets:read",
                "users:read",
                "users:update"
            });

        migrationBuilder.InsertData(
            table: "roles",
            column: "name",
            values: new object[]
            {
                "Administrator",
                "Member"
            });

        migrationBuilder.InsertData(
            table: "role_permissions",
            columns: new[] { "permission_code", "role_name" },
            values: new object[,]
            {
                { "carts:add", "Administrator" },
                { "carts:add", "Member" },
                { "carts:read", "Administrator" },
                { "carts:read", "Member" },
                { "carts:remove", "Administrator" },
                { "carts:remove", "Member" },
                { "categories:read", "Administrator" },
                { "categories:update", "Administrator" },
                { "event-statistics:read", "Administrator" },
                { "events:read", "Administrator" },
                { "events:search", "Administrator" },
                { "events:search", "Member" },
                { "events:update", "Administrator" },
                { "orders:create", "Administrator" },
                { "orders:create", "Member" },
                { "orders:read", "Administrator" },
                { "orders:read", "Member" },
                { "ticket-types:read", "Administrator" },
                { "ticket-types:read", "Member" },
                { "ticket-types:update", "Administrator" },
                { "tickets:check-in", "Administrator" },
                { "tickets:check-in", "Member" },
                { "tickets:read", "Administrator" },
                { "tickets:read", "Member" },
                { "users:read", "Administrator" },
                { "users:read", "Member" },
                { "users:update", "Administrator" },
                { "users:update", "Member" }
            });

        migrationBuilder.CreateIndex(
            name: "ix_role_permissions_role_name",
            table: "role_permissions",
            column: "role_name");

        migrationBuilder.CreateIndex(
            name: "ix_user_roles_user_id",
            table: "user_roles",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_users_email",
            table: "users",
            column: "email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_users_identity_id",
            table: "users",
            column: "identity_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "inbox_message_consumers");

        migrationBuilder.DropTable(
            name: "inbox_messages");

        migrationBuilder.DropTable(
            name: "outbox_message_consumers");

        migrationBuilder.DropTable(
            name: "outbox_messages");

        migrationBuilder.DropTable(
            name: "role_permissions");

        migrationBuilder.DropTable(
            name: "user_roles");

        migrationBuilder.DropTable(
            name: "permissions");

        migrationBuilder.DropTable(
            name: "roles");

        migrationBuilder.DropTable(
            name: "users");
    }
}
