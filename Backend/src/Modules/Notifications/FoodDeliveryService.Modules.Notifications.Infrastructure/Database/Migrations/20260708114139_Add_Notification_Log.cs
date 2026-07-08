using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class Add_Notification_Log : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "channel",
            table: "notifications",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<DateTime>(
            name: "created_on_utc",
            table: "notifications",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.AddColumn<string>(
            name: "error",
            table: "notifications",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "recipient_email",
            table: "notifications",
            type: "character varying(300)",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<Guid>(
            name: "recipient_user_id",
            table: "notifications",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "sent_on_utc",
            table: "notifications",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "status",
            table: "notifications",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "subject",
            table: "notifications",
            type: "character varying(500)",
            maxLength: 500,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "type",
            table: "notifications",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "ix_notifications_recipient_user_id",
            table: "notifications",
            column: "recipient_user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_notifications_recipient_user_id",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "channel",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "created_on_utc",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "error",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "recipient_email",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "recipient_user_id",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "sent_on_utc",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "status",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "subject",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "type",
            table: "notifications");
    }
}
