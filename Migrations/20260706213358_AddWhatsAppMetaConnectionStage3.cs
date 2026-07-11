using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppMetaConnectionStage3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GraphApiVersion",
                table: "WhatsAppSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "WhatsAppSettings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastErrorAt",
                table: "WhatsAppSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastWebhookReceivedAt",
                table: "WhatsAppSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WebhookVerifiedAt",
                table: "WhatsAppSettings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GraphApiVersion",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "LastErrorAt",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "LastWebhookReceivedAt",
                table: "WhatsAppSettings");

            migrationBuilder.DropColumn(
                name: "WebhookVerifiedAt",
                table: "WhatsAppSettings");
        }
    }
}
