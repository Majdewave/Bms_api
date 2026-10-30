using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clienta.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingDeliveryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryChannel",
                table: "ExternalAccountingOperations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchiveAttemptedAt",
                table: "ExternalAccountingDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchiveErrorCode",
                table: "ExternalAccountingDocuments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchiveStatus",
                table: "ExternalAccountingDocuments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryAttemptedAt",
                table: "ExternalAccountingDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryChannel",
                table: "ExternalAccountingDocuments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "DeliveryErrorCode",
                table: "ExternalAccountingDocuments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                table: "ExternalAccountingDocuments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotRequested");

            migrationBuilder.AddColumn<Guid>(
                name: "ExternalAccountingDocumentId",
                table: "ClientFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "ClientFiles",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageProvider",
                table: "ClientFiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientFiles_ExternalAccountingDocumentId",
                table: "ClientFiles",
                column: "ExternalAccountingDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientFiles_Tenant_ExternalAccountingDocument",
                table: "ClientFiles",
                columns: new[] { "TenantId", "ExternalAccountingDocumentId" },
                unique: true,
                filter: "\"ExternalAccountingDocumentId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientFiles_ExternalAccountingDocuments_ExternalAccountingD~",
                table: "ClientFiles",
                column: "ExternalAccountingDocumentId",
                principalTable: "ExternalAccountingDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientFiles_ExternalAccountingDocuments_ExternalAccountingD~",
                table: "ClientFiles");

            migrationBuilder.DropIndex(
                name: "IX_ClientFiles_ExternalAccountingDocumentId",
                table: "ClientFiles");

            migrationBuilder.DropIndex(
                name: "IX_ClientFiles_Tenant_ExternalAccountingDocument",
                table: "ClientFiles");

            migrationBuilder.DropColumn(
                name: "DeliveryChannel",
                table: "ExternalAccountingOperations");

            migrationBuilder.DropColumn(
                name: "ArchiveAttemptedAt",
                table: "ExternalAccountingDocuments");

            migrationBuilder.DropColumn(
                name: "ArchiveErrorCode",
                table: "ExternalAccountingDocuments");

            migrationBuilder.DropColumn(
                name: "ArchiveStatus",
                table: "ExternalAccountingDocuments");

            migrationBuilder.DropColumn(
                name: "DeliveryAttemptedAt",
                table: "ExternalAccountingDocuments");

            migrationBuilder.DropColumn(
                name: "DeliveryChannel",
                table: "ExternalAccountingDocuments");

            migrationBuilder.DropColumn(
                name: "DeliveryErrorCode",
                table: "ExternalAccountingDocuments");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                table: "ExternalAccountingDocuments");

            migrationBuilder.DropColumn(
                name: "ExternalAccountingDocumentId",
                table: "ClientFiles");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "ClientFiles");

            migrationBuilder.DropColumn(
                name: "StorageProvider",
                table: "ClientFiles");
        }
    }
}

