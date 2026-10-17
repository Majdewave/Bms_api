using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSpecificTrialDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BusinessTrialDays",
                table: "PlatformSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicTrialDays",
                table: "PlatformSettings",
                type: "integer",
                nullable: true);

            // Preserve each existing row's currently configured trial duration.
            migrationBuilder.Sql("UPDATE \"PlatformSettings\" SET \"ClinicTrialDays\" = \"DefaultTrialDays\", \"BusinessTrialDays\" = \"DefaultTrialDays\";");

            migrationBuilder.AlterColumn<int>(
                name: "BusinessTrialDays",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicTrialDays",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessTrialDays",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ClinicTrialDays",
                table: "PlatformSettings");
        }
    }
}
