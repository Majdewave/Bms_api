using System;
using Clienta.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261020120000_AddExternalAccountingPaymentLink")]
public partial class AddExternalAccountingPaymentLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "PaymentId",
            table: "ExternalAccountingDocuments",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_Payments_TenantId_InvoiceId_Id",
            table: "Payments",
            columns: new[] { "TenantId", "InvoiceId", "Id" });

        migrationBuilder.AddCheckConstraint(
            name: "CK_ExternalAccountingDocuments_PurposePayment",
            table: "ExternalAccountingDocuments",
            sql: "(\"DocumentType\" = 'Invoice' AND \"PaymentId\" IS NULL) OR (\"DocumentType\" IN ('Receipt', 'CombinedInvoiceReceipt') AND \"PaymentId\" IS NOT NULL)");

        migrationBuilder.DropIndex(
            name: "IX_ExternalAccountingDocuments_TenantId_Provider_InvoiceId_Doc~",
            table: "ExternalAccountingDocuments");

        migrationBuilder.CreateIndex(
            name: "IX_ExternalAccountingDocuments_TenantId_InvoiceId_PaymentId",
            table: "ExternalAccountingDocuments",
            columns: new[] { "TenantId", "InvoiceId", "PaymentId" });

        migrationBuilder.CreateIndex(
            name: "IX_ExternalAccountingDocuments_InvoicePurpose",
            table: "ExternalAccountingDocuments",
            columns: new[] { "TenantId", "Provider", "InvoiceId" },
            unique: true,
            filter: "\"DocumentType\" IN ('Invoice', 'CombinedInvoiceReceipt')");

        migrationBuilder.CreateIndex(
            name: "IX_ExternalAccountingDocuments_PaymentPurpose",
            table: "ExternalAccountingDocuments",
            columns: new[] { "TenantId", "Provider", "PaymentId", "DocumentType" },
            unique: true,
            filter: "\"PaymentId\" IS NOT NULL AND \"DocumentType\" = 'Receipt'");

        migrationBuilder.AddForeignKey(
            name: "FK_ExternalAccountingDocuments_Payments_TenantId_InvoiceId_PaymentId",
            table: "ExternalAccountingDocuments",
            columns: new[] { "TenantId", "InvoiceId", "PaymentId" },
            principalTable: "Payments",
            principalColumns: new[] { "TenantId", "InvoiceId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"ExternalAccountingDocuments\" WHERE \"PaymentId\" IS NOT NULL) THEN RAISE EXCEPTION 'Cannot remove ExternalAccountingDocuments.PaymentId while accounting documents reference payments.'; END IF; END $$;");

        migrationBuilder.DropForeignKey(
            name: "FK_ExternalAccountingDocuments_Payments_TenantId_InvoiceId_PaymentId",
            table: "ExternalAccountingDocuments");

        migrationBuilder.DropIndex(
            name: "IX_ExternalAccountingDocuments_TenantId_InvoiceId_PaymentId",
            table: "ExternalAccountingDocuments");

        migrationBuilder.DropIndex(
            name: "IX_ExternalAccountingDocuments_InvoicePurpose",
            table: "ExternalAccountingDocuments");

        migrationBuilder.DropIndex(
            name: "IX_ExternalAccountingDocuments_PaymentPurpose",
            table: "ExternalAccountingDocuments");

        migrationBuilder.DropCheckConstraint(
            name: "CK_ExternalAccountingDocuments_PurposePayment",
            table: "ExternalAccountingDocuments");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_Payments_TenantId_InvoiceId_Id",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "PaymentId",
            table: "ExternalAccountingDocuments");

        migrationBuilder.CreateIndex(
            name: "IX_ExternalAccountingDocuments_TenantId_Provider_InvoiceId_Doc~",
            table: "ExternalAccountingDocuments",
            columns: new[] { "TenantId", "Provider", "InvoiceId", "DocumentType" },
            unique: true);
    }
}
