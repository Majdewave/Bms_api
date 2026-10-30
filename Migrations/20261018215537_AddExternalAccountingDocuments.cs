using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalAccountingDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalAccountingDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExternalDocumentType = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalDocumentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalDocumentNumber = table.Column<int>(type: "integer", nullable: true),
                    ExternalCustomerId = table.Column<int>(type: "integer", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    ExternalAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    ProviderResponseCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAccountingDocuments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAccountingDocuments_TenantId_Provider_ExternalDocum~",
                table: "ExternalAccountingDocuments",
                columns: new[] { "TenantId", "Provider", "ExternalDocumentId" },
                unique: true,
                filter: "\"ExternalDocumentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAccountingDocuments_TenantId_Provider_InvoiceId_Doc~",
                table: "ExternalAccountingDocuments",
                columns: new[] { "TenantId", "Provider", "InvoiceId", "DocumentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAccountingDocuments_TenantId_Provider_RequestRefere~",
                table: "ExternalAccountingDocuments",
                columns: new[] { "TenantId", "Provider", "RequestReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalAccountingDocuments");
        }
    }
}
