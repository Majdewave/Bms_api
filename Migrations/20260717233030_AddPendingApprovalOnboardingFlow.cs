using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingApprovalOnboardingFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguage",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialStartsAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredLanguage",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrialStartsAt",
                table: "Tenants");
        }
    }
}
