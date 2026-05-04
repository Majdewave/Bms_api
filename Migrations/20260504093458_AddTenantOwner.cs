using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTrialExpiredEmailSent",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_OwnerUserId",
                table: "Tenants",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Users_OwnerUserId",
                table: "Tenants",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_Users_OwnerUserId",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_OwnerUserId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IsTrialExpiredEmailSent",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Tenants");
        }
    }
}
