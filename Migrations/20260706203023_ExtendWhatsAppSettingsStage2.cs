using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExtendWhatsAppSettingsStage2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessName",
                table: "WhatsAppSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConnectedSince",
                table: "WhatsAppSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionStatus",
                table: "WhatsAppSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DisplayPhoneNumber",
                table: "WhatsAppSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivity",
                table: "WhatsAppSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncAt",
                table: "WhatsAppSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TokenExpiresAt",
                table: "WhatsAppSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookUrl",
                table: "WhatsAppSettings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WebhookVerified",
                table: "WhatsAppSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessName",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "ConnectedSince",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "ConnectionStatus",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "DisplayPhoneNumber",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "LastActivity",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "LastSyncAt",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "TokenExpiresAt",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "WebhookUrl",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "WebhookVerified",
                table: "WhatsAppSettings");
        }
    }
}
