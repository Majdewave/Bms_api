using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProfessionalInvoiceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessEmail",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessName",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessPhone",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LogoBase64",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvoiceLineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLineItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLineItems_InvoiceId",
                table: "InvoiceLineItems",
                column: "InvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceLineItems");

            migrationBuilder.DropColumn(
                name: "BusinessAddress",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BusinessEmail",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BusinessName",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BusinessPhone",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LogoBase64",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Invoices");
        }
    }
}
