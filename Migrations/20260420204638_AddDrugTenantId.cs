using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDrugTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Drugs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.UpdateData(
                table: "Drugs",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                column: "TenantId",
                value: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.UpdateData(
                table: "Drugs",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                column: "TenantId",
                value: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.UpdateData(
                table: "Drugs",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                column: "TenantId",
                value: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.UpdateData(
                table: "Drugs",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                column: "TenantId",
                value: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Drugs");
        }
    }
}
