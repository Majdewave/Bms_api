using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalAccountingOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalAccountingOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalDocumentType = table.Column<int>(type: "integer", nullable: true),
                    ExternalDocumentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalDocumentNumber = table.Column<int>(type: "integer", nullable: true),
                    ExternalCustomerId = table.Column<int>(type: "integer", nullable: true),
                    ExternalAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    ProviderResponseCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAccountingOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalAccountingOperations_Payments_TenantId_InvoiceId_Pa~",
                        columns: x => new { x.TenantId, x.InvoiceId, x.PaymentId },
                        principalTable: "Payments",
                        principalColumns: new[] { "TenantId", "InvoiceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAccountingOperations_TenantId_InvoiceId_PaymentId",
                table: "ExternalAccountingOperations",
                columns: new[] { "TenantId", "InvoiceId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAccountingOperations_TenantId_InvoiceId_Status",
                table: "ExternalAccountingOperations",
                columns: new[] { "TenantId", "InvoiceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAccountingOperations_Tenant_Provider_IdempotencyKey",
                table: "ExternalAccountingOperations",
                columns: new[] { "TenantId", "Provider", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAccountingOperations_Tenant_Provider_RequestReference",
                table: "ExternalAccountingOperations",
                columns: new[] { "TenantId", "Provider", "RequestReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalAccountingOperations");
        }
    }
}
