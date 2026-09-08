using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppNotDocumentedMedicalImagingFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MedicalImagingEnabled",
                table: "TenantFeatures",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotDocumentedEnabled",
                table: "TenantFeatures",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppEnabled",
                table: "TenantFeatures",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MedicalImagingEnabled",
                table: "TenantFeatures");

            migrationBuilder.DropColumn(
                name: "NotDocumentedEnabled",
                table: "TenantFeatures");

            migrationBuilder.DropColumn(
                name: "WhatsAppEnabled",
                table: "TenantFeatures");
        }
    }
}
