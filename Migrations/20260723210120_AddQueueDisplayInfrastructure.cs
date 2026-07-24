using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueDisplayInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "QueueDisplayEnabled",
                table: "TenantFeatures",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "QueueDisplaySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicToken = table.Column<string>(type: "text", nullable: false),
                    PrivacyMode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Theme = table.Column<string>(type: "text", nullable: false, defaultValue: "default"),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    AdvertisementImageUrl = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueDisplaySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueDisplaySettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueueDisplaySettings_PublicToken",
                table: "QueueDisplaySettings",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueDisplaySettings_TenantId",
                table: "QueueDisplaySettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueueDisplaySettings");

            migrationBuilder.DropColumn(
                name: "QueueDisplayEnabled",
                table: "TenantFeatures");
        }
    }
}
