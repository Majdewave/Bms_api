using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class HardenWhatsAppMetaConnectionStage3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppOAuthStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NonceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppOAuthStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppSettings_ConnectionStatus_TokenExpiresAt",
                table: "WhatsAppSettings",
                columns: new[] { "ConnectionStatus", "TokenExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOAuthStates_TenantId_ExpiresAt",
                table: "WhatsAppOAuthStates",
                columns: new[] { "TenantId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOAuthStates_TenantId_NonceHash",
                table: "WhatsAppOAuthStates",
                columns: new[] { "TenantId", "NonceHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOAuthStates_TenantId_UsedAt",
                table: "WhatsAppOAuthStates",
                columns: new[] { "TenantId", "UsedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppOAuthStates");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppSettings_ConnectionStatus_TokenExpiresAt",
                table: "WhatsAppSettings");
        }
    }
}
