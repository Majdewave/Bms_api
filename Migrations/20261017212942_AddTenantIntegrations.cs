using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantIntegrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccountingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EncryptedCredential = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConnectionStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastConnectionTestAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastConnectionSucceededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastConnectionErrorCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantIntegrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantIntegrations_TenantId_Provider",
                table: "TenantIntegrations",
                columns: new[] { "TenantId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantIntegrations");
        }
    }
}
