using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class RefineQueueDisplaySettingsEnums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LogoUrl",
                table: "QueueDisplaySettings",
                newName: "LogoOverrideUrl");

            migrationBuilder.AddColumn<string>(
                name: "VoiceSettingsJson",
                table: "QueueDisplaySettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThemeTemp",
                table: "QueueDisplaySettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
                UPDATE ""QueueDisplaySettings""
                SET ""ThemeTemp"" = CASE LOWER(COALESCE(""Theme"", 'default'))
                    WHEN 'light' THEN 1
                    WHEN 'dark' THEN 2
                    WHEN 'blue' THEN 3
                    WHEN 'calm' THEN 3
                    WHEN 'contrast' THEN 2
                    ELSE 0
                END;
            ");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "QueueDisplaySettings");

            migrationBuilder.RenameColumn(
                name: "ThemeTemp",
                table: "QueueDisplaySettings",
                newName: "Theme");

            migrationBuilder.AddColumn<int>(
                name: "PrivacyModeTemp",
                table: "QueueDisplaySettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
                UPDATE ""QueueDisplaySettings""
                SET ""PrivacyModeTemp"" = CASE
                    WHEN ""PrivacyMode"" = TRUE THEN 2
                    ELSE 0
                END;
            ");

            migrationBuilder.DropColumn(
                name: "PrivacyMode",
                table: "QueueDisplaySettings");

            migrationBuilder.RenameColumn(
                name: "PrivacyModeTemp",
                table: "QueueDisplaySettings",
                newName: "PrivacyMode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LogoOverrideUrl",
                table: "QueueDisplaySettings",
                newName: "LogoUrl");

            migrationBuilder.DropColumn(
                name: "VoiceSettingsJson",
                table: "QueueDisplaySettings");

            migrationBuilder.AddColumn<string>(
                name: "ThemeTemp",
                table: "QueueDisplaySettings",
                type: "text",
                nullable: false,
                defaultValue: "default");

            migrationBuilder.Sql(@"
                UPDATE ""QueueDisplaySettings""
                SET ""ThemeTemp"" = CASE ""Theme""
                    WHEN 1 THEN 'light'
                    WHEN 2 THEN 'dark'
                    WHEN 3 THEN 'blue'
                    ELSE 'default'
                END;
            ");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "QueueDisplaySettings");

            migrationBuilder.RenameColumn(
                name: "ThemeTemp",
                table: "QueueDisplaySettings",
                newName: "Theme");

            migrationBuilder.AddColumn<bool>(
                name: "PrivacyModeTemp",
                table: "QueueDisplaySettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"
                UPDATE ""QueueDisplaySettings""
                SET ""PrivacyModeTemp"" = CASE
                    WHEN ""PrivacyMode"" = 2 THEN TRUE
                    ELSE FALSE
                END;
            ");

            migrationBuilder.DropColumn(
                name: "PrivacyMode",
                table: "QueueDisplaySettings");

            migrationBuilder.RenameColumn(
                name: "PrivacyModeTemp",
                table: "QueueDisplaySettings",
                newName: "PrivacyMode");
        }
    }
}
