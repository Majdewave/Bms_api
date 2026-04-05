using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateAndServiceToClientConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ServiceId",
                table: "ClientConsents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "ClientConsents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientConsents_ServiceId",
                table: "ClientConsents",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientConsents_TemplateId",
                table: "ClientConsents",
                column: "TemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientConsents_ConsentTemplates_TemplateId",
                table: "ClientConsents",
                column: "TemplateId",
                principalTable: "ConsentTemplates",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientConsents_Services_ServiceId",
                table: "ClientConsents",
                column: "ServiceId",
                principalTable: "Services",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientConsents_ConsentTemplates_TemplateId",
                table: "ClientConsents");

            migrationBuilder.DropForeignKey(
                name: "FK_ClientConsents_Services_ServiceId",
                table: "ClientConsents");

            migrationBuilder.DropIndex(
                name: "IX_ClientConsents_ServiceId",
                table: "ClientConsents");

            migrationBuilder.DropIndex(
                name: "IX_ClientConsents_TemplateId",
                table: "ClientConsents");

            migrationBuilder.DropColumn(
                name: "ServiceId",
                table: "ClientConsents");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "ClientConsents");
        }
    }
}
