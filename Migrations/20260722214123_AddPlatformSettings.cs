using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformName = table.Column<string>(type: "text", nullable: false),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    SupportEmail = table.Column<string>(type: "text", nullable: false),
                    SupportPhone = table.Column<string>(type: "text", nullable: true),
                    AllowRegistrations = table.Column<bool>(type: "boolean", nullable: false),
                    RequireManualApproval = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultTrialDays = table.Column<int>(type: "integer", nullable: false),
                    DefaultPlan = table.Column<string>(type: "text", nullable: false),
                    SenderName = table.Column<string>(type: "text", nullable: false),
                    SenderEmail = table.Column<string>(type: "text", nullable: false),
                    SmtpHost = table.Column<string>(type: "text", nullable: true),
                    SmtpPort = table.Column<int>(type: "integer", nullable: true),
                    SmtpUsername = table.Column<string>(type: "text", nullable: true),
                    SmtpPassword = table.Column<string>(type: "text", nullable: true),
                    SmtpUseSsl = table.Column<bool>(type: "boolean", nullable: false),
                    WelcomeEmailTemplate = table.Column<string>(type: "text", nullable: true),
                    ApprovalEmailTemplate = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric", nullable: false),
                    BillingDefaultPlan = table.Column<string>(type: "text", nullable: false),
                    TrialReminderDays = table.Column<int>(type: "integer", nullable: false),
                    MinimumPasswordLength = table.Column<int>(type: "integer", nullable: false),
                    RequireUppercasePassword = table.Column<bool>(type: "boolean", nullable: false),
                    RequireNumberPassword = table.Column<bool>(type: "boolean", nullable: false),
                    RequireSpecialCharacterPassword = table.Column<bool>(type: "boolean", nullable: false),
                    SessionTimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    ForcePasswordReset = table.Column<bool>(type: "boolean", nullable: false),
                    MfaEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    StripeEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WhatsAppEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SendGridEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MaintenanceMode = table.Column<bool>(type: "boolean", nullable: false),
                    ReadOnlyMode = table.Column<bool>(type: "boolean", nullable: false),
                    AuditLogEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformSettings");
        }
    }
}
