using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIdToDrugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Drugs",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                column: "TenantId",
                value: new Guid("40aff269-58db-4d97-b391-ccbb701cd458"));

            migrationBuilder.UpdateData(
                table: "Drugs",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                column: "TenantId",
                value: new Guid("40aff269-58db-4d97-b391-ccbb701cd458"));

            migrationBuilder.UpdateData(
                table: "Drugs",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                column: "TenantId",
                value: new Guid("40aff269-58db-4d97-b391-ccbb701cd458"));

            migrationBuilder.UpdateData(
                table: "Drugs",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                column: "TenantId",
                value: new Guid("40aff269-58db-4d97-b391-ccbb701cd458"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
