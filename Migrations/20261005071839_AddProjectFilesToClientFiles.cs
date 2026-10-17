using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFilesToClientFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "ClientFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ClientFiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientFiles_ProjectId",
                table: "ClientFiles",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientFiles_TenantId_ProjectId",
                table: "ClientFiles",
                columns: new[] { "TenantId", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ClientFiles_Projects_ProjectId",
                table: "ClientFiles",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientFiles_Projects_ProjectId",
                table: "ClientFiles");

            migrationBuilder.DropIndex(
                name: "IX_ClientFiles_ProjectId",
                table: "ClientFiles");

            migrationBuilder.DropIndex(
                name: "IX_ClientFiles_TenantId_ProjectId",
                table: "ClientFiles");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "ClientFiles");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "ClientFiles");
        }
    }
}
