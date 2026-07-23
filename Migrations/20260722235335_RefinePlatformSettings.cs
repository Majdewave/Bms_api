using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class RefinePlatformSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalEmailTemplate",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "AuditLogEnabled",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "BillingDefaultPlan",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPlan",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ForcePasswordReset",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "MaintenanceMode",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "MinimumPasswordLength",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "PlatformName",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ReadOnlyMode",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "RequireNumberPassword",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "RequireSpecialCharacterPassword",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "RequireUppercasePassword",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SenderEmail",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmtpHost",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmtpPassword",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmtpPort",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmtpUsername",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SmtpUseSsl",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "StripeEnabled",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "WelcomeEmailTemplate",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SenderName",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SendGridEnabled",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "SessionTimeoutMinutes",
                table: "PlatformSettings");

            migrationBuilder.AddColumn<bool>(
                name: "EnableBilling",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableHelpCenter",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProAnnualPrice",
                table: "PlatformSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 460m);

            migrationBuilder.AddColumn<string>(
                name: "ProDescription",
                table: "PlatformSettings",
                type: "text",
                nullable: false,
                defaultValue: "Clienta Pro for growing teams");

            migrationBuilder.AddColumn<int>(
                name: "ProDisplayOrder",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "ProEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProMonthlyPrice",
                table: "PlatformSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 46m);

            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                table: "PlatformSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableBilling",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "EnableHelpCenter",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ProAnnualPrice",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ProDescription",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ProDisplayOrder",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ProEnabled",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "ProMonthlyPrice",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                table: "PlatformSettings");

            migrationBuilder.AddColumn<string>(
                name: "ApprovalEmailTemplate",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AuditLogEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BillingDefaultPlan",
                table: "PlatformSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "PlatformSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DefaultPlan",
                table: "PlatformSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ForcePasswordReset",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MaintenanceMode",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MinimumPasswordLength",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PlatformName",
                table: "PlatformSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ReadOnlyMode",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireNumberPassword",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireSpecialCharacterPassword",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireUppercasePassword",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SenderEmail",
                table: "PlatformSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SmtpHost",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpPassword",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmtpPort",
                table: "PlatformSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpUsername",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpUseSsl",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "StripeEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "PlatformSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WelcomeEmailTemplate",
                table: "PlatformSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderName",
                table: "PlatformSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SendGridEnabled",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SessionTimeoutMinutes",
                table: "PlatformSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
