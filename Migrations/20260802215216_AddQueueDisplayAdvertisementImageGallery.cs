using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueDisplayAdvertisementImageGallery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QueueDisplayAdvertisementImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueDisplaySettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueDisplayAdvertisementImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueDisplayAdvertisementImages_QueueDisplaySettings_QueueD~",
                        column: x => x.QueueDisplaySettingsId,
                        principalTable: "QueueDisplaySettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueueDisplayAdvertisementImages_QueueDisplaySettingsId",
                table: "QueueDisplayAdvertisementImages",
                column: "QueueDisplaySettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueDisplayAdvertisementImages_TenantId_QueueDisplaySettin~",
                table: "QueueDisplayAdvertisementImages",
                columns: new[] { "TenantId", "QueueDisplaySettingsId", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueueDisplayAdvertisementImages");
        }
    }
}
